public class User
{
  public required string UserId { get; set; } = Guid.NewGuid().ToString();
  public required string FirstName { get; set; }
  public required string LastName { get; set; }
  public required string Email { get; set; }
  public string? Phone { get; set; }
  public required string Username { get; set; }
  public required string Password { get; set; }
  public required string CreatedDate { get; set; } = ""; // You can also use DateTime if your DB supports it

  // Navigation properties for nested objects
  public required Role Role { get; set; }
  public required List<Permission> Permissions { get; set; }
}

public class Role
{
  public required string RoleId { get; set; }
  public required string RoleName { get; set; }
}

public class Permission
{
  public required string PermissionId { get; set; }
  public required string PermissionName { get; set; }
}
