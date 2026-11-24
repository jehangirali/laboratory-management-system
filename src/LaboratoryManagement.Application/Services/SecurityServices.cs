using System.Security.Cryptography;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public sealed class CurrentUser : ICurrentUser
{
    private HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _roles = new();

    public bool IsAuthenticated { get; private set; }
    public int UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Designation { get; private set; }
    public string? SignatureText { get; private set; }
    public IReadOnlyCollection<string> Roles => _roles;

    public bool Has(string permission) => IsAuthenticated && _permissions.Contains(permission);

    public void Require(string permission)
    {
        if (!Has(permission)) throw new PermissionDeniedException(permission);
    }

    public void Set(int userId, string username, string fullName, string? designation, string? signature,
                    IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        UserId = userId; Username = username; FullName = fullName; Designation = designation; SignatureText = signature;
        _roles = roles.ToList();
        _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        IsAuthenticated = true;
    }

    public void Clear()
    {
        IsAuthenticated = false; UserId = 0; Username = FullName = string.Empty; Designation = SignatureText = null;
        _roles = new(); _permissions = new(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>PBKDF2-HMAC-SHA256 with per-password random salt. Format: v1.iterations.salt.hash (base64).</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        try
        {
            var parts = hash.Split('.');
            if (parts.Length != 4 || parts[0] != "v1") return false;
            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

public sealed class AuditService : IAuditService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    public AuditService(IAppDbContextFactory factory, ICurrentUser user) { _factory = factory; _user = user; }

    public void Add(IAppDbContext db, string action, string entityType, string? entityId, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            At = DateTime.UtcNow,
            UserId = _user.IsAuthenticated ? _user.UserId : null,
            Username = _user.IsAuthenticated ? _user.Username : "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is { Length: > 3900 } ? details[..3900] : details
        });
    }

    public async Task LogAsync(string action, string entityType, string? entityId, string? details = null)
    {
        await using var db = _factory.Create();
        Add(db, action, entityType, entityId, details);
        await db.SaveChangesAsync();
    }
}

public sealed class SettingsService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private Dictionary<string, string> _cache = new();

    public SettingsService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit)
    { _factory = factory; _user = user; _audit = audit; }

    public async Task RefreshAsync()
    {
        await using var db = _factory.Create();
        _cache = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public string Get(string key, string fallback = "") => _cache.TryGetValue(key, out var v) ? v : fallback;
    public bool GetBool(string key, bool fallback = false)
        => _cache.TryGetValue(key, out var v) ? bool.TryParse(v, out var b) && b : fallback;
    public int GetInt(string key, int fallback = 0)
        => _cache.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;
    public double GetDouble(string key, double fallback = 0)
        => _cache.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public async Task<List<AppSetting>> GetAllAsync()
    {
        await using var db = _factory.Create();
        return await db.AppSettings.AsNoTracking().OrderBy(s => s.Group).ThenBy(s => s.Key).ToListAsync();
    }

    public async Task SaveAsync(IEnumerable<AppSetting> changed)
    {
        _user.Require(Permissions.SettingsManage);
        await using var db = _factory.Create();
        var all = await db.AppSettings.ToDictionaryAsync(s => s.Key);
        foreach (var s in changed)
        {
            if (!all.TryGetValue(s.Key, out var existing)) continue;
            if (existing.Value == s.Value) continue;
            _audit.Add(db, "Setting changed", "AppSetting", s.Key, $"'{existing.Value}' -> '{s.Value}'");
            existing.Value = s.Value;
        }
        await db.SaveChangesAsync();
        await RefreshAsync();
    }

    public async Task SetInternalAsync(string key, string value)
    {
        await using var db = _factory.Create();
        var s = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (s == null) return;
        s.Value = value;
        await db.SaveChangesAsync();
        _cache[key] = value;
    }
}

public sealed class AuthService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _current;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly SettingsService _settings;

    public AuthService(IAppDbContextFactory factory, ICurrentUser current, IPasswordHasher hasher,
                       IAuditService audit, SettingsService settings)
    { _factory = factory; _current = current; _hasher = hasher; _audit = audit; _settings = settings; }

    public async Task<bool> AdminExistsAsync()
    {
        await using var db = _factory.Create();
        return await db.UserRoles.AnyAsync(ur => ur.Role.Name == SystemRoles.Administrator && ur.User.IsActive);
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return "Password must be at least 8 characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Password must contain letters and digits.";
        return null;
    }

    public async Task CreateFirstAdministratorAsync(string username, string fullName, string password)
    {
        if (await AdminExistsAsync()) throw new BusinessRuleException("An administrator already exists.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
            throw new BusinessRuleException("Username and full name are required.");
        var pwError = ValidatePassword(password);
        if (pwError != null) throw new BusinessRuleException(pwError);

        await using var db = _factory.Create();
        var role = await db.Roles.FirstAsync(r => r.Name == SystemRoles.Administrator);
        var user = new User
        {
            Username = username.Trim(),
            FullName = fullName.Trim(),
            PasswordHash = _hasher.Hash(password),
            Designation = "System Administrator",
            IsActive = true
        };
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        db.AuditLogs.Add(new AuditLog { Username = "setup", Action = "First administrator created", EntityType = "User", EntityId = user.Username });
        await db.SaveChangesAsync();
    }

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        await using var db = _factory.Create();
        var uname = (username ?? "").Trim();
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == uname);

        if (user == null || !_hasher.Verify(password ?? "", user.PasswordHash))
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user?.Id, Username = uname, Event = "FailedLogin", MachineName = Environment.MachineName });
            await db.SaveChangesAsync();
            return (false, "Invalid username or password.");
        }
        if (!user.IsActive)
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Username = uname, Event = "FailedLogin", Details = "Account disabled", MachineName = Environment.MachineName });
            await db.SaveChangesAsync();
            return (false, "This account is disabled. Please contact an administrator.");
        }

        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var perms = await db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code).Distinct().ToListAsync();

        user.LastLoginAt = DateTime.UtcNow;
        db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Username = user.Username, Event = "Login", MachineName = Environment.MachineName });
        await db.SaveChangesAsync();

        _current.Set(user.Id, user.Username, user.FullName, user.Designation, user.SignatureText,
                     user.UserRoles.Select(ur => ur.Role.Name), perms);
        await _settings.RefreshAsync();
        return (true, user.MustChangePassword ? "MUSTCHANGE" : "OK");
    }

    public async Task LogoutAsync()
    {
        if (!_current.IsAuthenticated) return;
        await using var db = _factory.Create();
        db.LoginHistory.Add(new LoginHistory { UserId = _current.UserId, Username = _current.Username, Event = "Logout", MachineName = Environment.MachineName });
        await db.SaveChangesAsync();
        _current.Clear();
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword)
    {
        if (!_current.IsAuthenticated) throw new PermissionDeniedException("login");
        var pwError = ValidatePassword(newPassword);
        if (pwError != null) throw new BusinessRuleException(pwError);
        await using var db = _factory.Create();
        var user = await db.Users.FirstAsync(u => u.Id == _current.UserId);
        if (!_hasher.Verify(currentPassword ?? "", user.PasswordHash))
            throw new BusinessRuleException("Current password is incorrect.");
        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        _audit.Add(db, "Password changed", "User", user.Username);
        await db.SaveChangesAsync();
    }
}

public sealed class UserEditDto
{
    public int? Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Designation { get; set; }
    public string? SignatureText { get; set; }
    public bool IsActive { get; set; } = true;
    public string? NewPassword { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

public sealed class UserService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;

    public UserService(IAppDbContextFactory factory, ICurrentUser user, IPasswordHasher hasher, IAuditService audit)
    { _factory = factory; _user = user; _hasher = hasher; _audit = audit; }

    public async Task<List<User>> GetUsersAsync()
    {
        _user.Require(Permissions.UsersManage);
        await using var db = _factory.Create();
        return await db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username).ToListAsync();
    }

    public async Task<List<Role>> GetRolesAsync()
    {
        await using var db = _factory.Create();
        return await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    public async Task<UserEditDto> GetAsync(int id)
    {
        _user.Require(Permissions.UsersManage);
        await using var db = _factory.Create();
        var u = await db.Users.AsNoTracking().Include(x => x.UserRoles).FirstAsync(x => x.Id == id);
        return new UserEditDto
        {
            Id = u.Id, Username = u.Username, FullName = u.FullName, Designation = u.Designation,
            SignatureText = u.SignatureText, IsActive = u.IsActive, RoleIds = u.UserRoles.Select(r => r.RoleId).ToList()
        };
    }

    public async Task SaveAsync(UserEditDto dto)
    {
        _user.Require(Permissions.UsersManage);
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.FullName))
            throw new BusinessRuleException("Username and full name are required.");
        if (dto.RoleIds.Count == 0) throw new BusinessRuleException("Assign at least one role.");

        await using var db = _factory.Create();
        var uname = dto.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == uname && u.Id != (dto.Id ?? 0)))
            throw new BusinessRuleException("This username is already in use.");

        User user;
        if (dto.Id is null)
        {
            var pwError = AuthService.ValidatePassword(dto.NewPassword);
            if (pwError != null) throw new BusinessRuleException(pwError);
            user = new User { Username = uname, PasswordHash = _hasher.Hash(dto.NewPassword!), MustChangePassword = true };
            db.Users.Add(user);
        }
        else
        {
            user = await db.Users.Include(u => u.UserRoles).FirstAsync(u => u.Id == dto.Id);
            if (!string.IsNullOrEmpty(dto.NewPassword))
            {
                var pwError = AuthService.ValidatePassword(dto.NewPassword);
                if (pwError != null) throw new BusinessRuleException(pwError);
                user.PasswordHash = _hasher.Hash(dto.NewPassword);
                user.MustChangePassword = true;
                _audit.Add(db, "Password reset", "User", user.Username);
            }
        }

        // Never allow the last active administrator to be removed or deactivated.
        var adminRoleId = await db.Roles.Where(r => r.Name == SystemRoles.Administrator).Select(r => r.Id).FirstAsync();
        var willBeAdmin = dto.IsActive && dto.RoleIds.Contains(adminRoleId);
        if (!willBeAdmin && dto.Id != null)
        {
            var otherAdmins = await db.UserRoles.CountAsync(ur => ur.RoleId == adminRoleId && ur.User.IsActive && ur.UserId != dto.Id);
            if (otherAdmins == 0) throw new BusinessRuleException("At least one active administrator must remain.");
        }

        user.Username = uname;
        user.FullName = dto.FullName.Trim();
        user.Designation = dto.Designation;
        user.SignatureText = dto.SignatureText;
        user.IsActive = dto.IsActive;

        var existing = user.UserRoles.ToList();
        foreach (var ur in existing.Where(x => !dto.RoleIds.Contains(x.RoleId))) user.UserRoles.Remove(ur);
        foreach (var rid in dto.RoleIds.Where(r => existing.All(x => x.RoleId != r)))
            user.UserRoles.Add(new UserRole { RoleId = rid });

        _audit.Add(db, dto.Id is null ? "User created" : "User updated", "User", uname,
                   $"Active={dto.IsActive}; Roles={string.Join(",", dto.RoleIds)}");
        await db.SaveChangesAsync();
    }

    public async Task<List<(Permission Permission, bool Granted)>> GetRolePermissionsAsync(int roleId)
    {
        _user.Require(Permissions.UsersManage);
        await using var db = _factory.Create();
        var granted = (await db.RolePermissions.Where(rp => rp.RoleId == roleId).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();
        var all = await db.Permissions.AsNoTracking().OrderBy(p => p.Group).ThenBy(p => p.Code).ToListAsync();
        return all.Select(p => (p, granted.Contains(p.Id))).ToList();
    }

    public async Task SaveRolePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        _user.Require(Permissions.UsersManage);
        await using var db = _factory.Create();
        var role = await db.Roles.Include(r => r.RolePermissions).FirstAsync(r => r.Id == roleId);
        var wanted = permissionIds.ToHashSet();
        if (role.Name == SystemRoles.Administrator)
        {
            // The administrator role must always be able to manage users and settings.
            var must = await db.Permissions.Where(p => p.Code == Permissions.UsersManage || p.Code == Permissions.SettingsManage)
                .Select(p => p.Id).ToListAsync();
            foreach (var m in must) wanted.Add(m);
        }
        foreach (var rp in role.RolePermissions.Where(x => !wanted.Contains(x.PermissionId)).ToList()) role.RolePermissions.Remove(rp);
        foreach (var pid in wanted.Where(p => role.RolePermissions.All(x => x.PermissionId != p)))
            role.RolePermissions.Add(new RolePermission { PermissionId = pid });
        _audit.Add(db, "Role permissions changed", "Role", role.Name, $"{wanted.Count} permissions");
        await db.SaveChangesAsync();
    }

    public async Task<List<LoginHistory>> GetLoginHistoryAsync(int take = 300)
    {
        _user.Require(Permissions.AuditView);
        await using var db = _factory.Create();
        return await db.LoginHistory.AsNoTracking().OrderByDescending(l => l.At).Take(take).ToListAsync();
    }

    public async Task<List<AuditLog>> GetAuditAsync(string? search, DateTime? fromLocal, DateTime? toLocal, int take = 500)
    {
        _user.Require(Permissions.AuditView);
        await using var db = _factory.Create();
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (fromLocal != null) { var f = fromLocal.Value.Date.ToUniversalTime(); q = q.Where(a => a.At >= f); }
        if (toLocal != null) { var t = toLocal.Value.Date.AddDays(1).ToUniversalTime(); q = q.Where(a => a.At < t); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.Action.Contains(s) || a.EntityType.Contains(s) || a.Username.Contains(s)
                          || (a.EntityId != null && a.EntityId.Contains(s)) || (a.Details != null && a.Details.Contains(s)));
        }
        return await q.OrderByDescending(a => a.At).Take(take).ToListAsync();
    }
}
