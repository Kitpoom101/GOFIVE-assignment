using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
  private readonly AppDbContext _db;
  private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
  {
    "Super admin",
    "admin",
    "employee"
  };

  private static readonly HashSet<string> AllowedPermissionNames = new(StringComparer.OrdinalIgnoreCase)
  {
    "read",
    "write",
    "delete"
  };

  public UsersController(AppDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  public async Task<IActionResult> GetUsers(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 6,
    [FromQuery] string? sortBy = "name",
    [FromQuery] string? sortDir = "asc",
    [FromQuery] string? search = null)
  {
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = _db.Users
      .Include(u => u.Role)
      .Include(u => u.Permissions)
      .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLower();
      query = query.Where(u =>
        ((u.FirstName ?? "") + " " + (u.LastName ?? "")).ToLower().Contains(term) ||
        (u.Username ?? "").ToLower().Contains(term) ||
        (u.Email ?? "").ToLower().Contains(term));
    }

    var isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
    query = (sortBy ?? "name").Trim().ToLower() switch
    {
      "username" => isDesc
        ? query.OrderByDescending(u => u.Username ?? "")
        : query.OrderBy(u => u.Username ?? ""),
      "email" => isDesc
        ? query.OrderByDescending(u => u.Email ?? "")
        : query.OrderBy(u => u.Email ?? ""),
      "createddate" => isDesc
        ? query.OrderByDescending(u => u.CreatedDate ?? "")
        : query.OrderBy(u => u.CreatedDate ?? ""),
      _ => isDesc
        ? query.OrderByDescending(u => (u.FirstName ?? "") + " " + (u.LastName ?? ""))
        : query.OrderBy(u => (u.FirstName ?? "") + " " + (u.LastName ?? "")),
    };

    var totalItems = await query.CountAsync();
    var users = await query
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .ToListAsync();

    return Ok(new
    {
      items = users,
      page,
      pageSize,
      totalItems,
      totalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
    });
  }

  [HttpGet("{id}")]
  public async Task<IActionResult> GetUser(string id)
  {
    var user = await _db.Users
        .Include(u => u.Role)
        .Include(u => u.Permissions)
        .FirstOrDefaultAsync(u => u.UserId == id);

    if (user == null)
    {
      return NotFound();
    }

    return Ok(user);
  }

  [HttpPost]
  public async Task<IActionResult> CreateUser([FromBody] User user)
  {
    if (user == null)
    {
      return BadRequest("User data is null.");
    }

    if (string.IsNullOrWhiteSpace(user.UserId))
    {
      user.UserId = Guid.NewGuid().ToString();
    }

    if (string.IsNullOrWhiteSpace(user.CreatedDate))
    {
      user.CreatedDate = DateTime.UtcNow.ToString("O");
    }

    var validationError = NormalizeAndValidateRoleAndPermissions(user);
    if (validationError != null)
    {
      return BadRequest(validationError);
    }

    user.Permissions ??= new List<Permission>();

    _db.Users.Add(user);
    await _db.SaveChangesAsync();

    // Returns a 201 Created status and points to the GetUser endpoint
    return CreatedAtAction(nameof(GetUser), new { id = user.UserId }, new
    {
      message = "User created successfully",
      data = user
    });
  }

  [HttpPut("{id}")]
  public async Task<IActionResult> UpdateUser(string id, [FromBody] User user)
  {
    if (id != user.UserId)
    {
      return BadRequest("User ID mismatch.");
    }

    var validationError = NormalizeAndValidateRoleAndPermissions(user);
    if (validationError != null)
    {
      return BadRequest(validationError);
    }

    _db.Entry(user).State = EntityState.Modified;

    try
    {
      await _db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
      if (!UserExists(id))
      {
        return NotFound();
      }
      else
      {
        throw;
      }
    }

    return Ok(new
    {
      message = "User updated successfully",
      data = user
    });
  }

  [HttpDelete("{id}")]
  public async Task<IActionResult> DeleteUser(string id)
  {
    var user = await _db.Users
      .Include(u => u.Role)
      .Include(u => u.Permissions)
      .FirstOrDefaultAsync(u => u.UserId == id);

    if (user == null)
    {
      return NotFound();
    }

    // Remove dependents first to avoid FK constraint violations.
    if (user.Permissions != null && user.Permissions.Count > 0)
    {
      _db.RemoveRange(user.Permissions);
    }

    if (user.Role != null)
    {
      _db.Remove(user.Role);
    }

    _db.Users.Remove(user);
    await _db.SaveChangesAsync();

    return Ok(new
    {
      message = "User deleted successfully",
      data = user
    });
  }

  // Helper method for the PUT request
  private bool UserExists(string id)
  {
    return _db.Users.Any(e => e.UserId == id);
  }

  private static string? NormalizeAndValidateRoleAndPermissions(User user)
  {
    if (user.Role == null || string.IsNullOrWhiteSpace(user.Role.RoleName))
    {
      return "Role is required.";
    }

    var normalizedRoleName = user.Role.RoleName.Trim();
    if (!AllowedRoles.Contains(normalizedRoleName))
    {
      return "Role must be one of: Super admin, admin, employee.";
    }

    user.Role.RoleName = normalizedRoleName;
    if (string.IsNullOrWhiteSpace(user.Role.RoleId))
    {
      user.Role.RoleId = Guid.NewGuid().ToString();
    }

    user.Permissions ??= new List<Permission>();
    var distinctPermissions = user.Permissions
      .Where(p => !string.IsNullOrWhiteSpace(p.PermissionName))
      .GroupBy(p => p.PermissionName.Trim(), StringComparer.OrdinalIgnoreCase)
      .Select(g => g.First())
      .ToList();

    foreach (var permission in distinctPermissions)
    {
      permission.PermissionName = permission.PermissionName.Trim().ToLowerInvariant();

      if (!AllowedPermissionNames.Contains(permission.PermissionName))
      {
        return "Permissions can only be: read, write, delete.";
      }

      if (string.IsNullOrWhiteSpace(permission.PermissionId))
      {
        permission.PermissionId = Guid.NewGuid().ToString();
      }
    }

    user.Permissions = distinctPermissions;
    return null;
  }
}
