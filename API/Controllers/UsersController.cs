using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
  private readonly AppDbContext _db;

  public UsersController(AppDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  public async Task<IActionResult> GetUsers()
  {
    // Includes nested Role and Permissions in the response
    var users = await _db.Users
        .Include(u => u.Role)
        .Include(u => u.Permissions)
        .ToListAsync();

    return Ok(users);
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

    user.Permissions ??= [];

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
    var user = await _db.Users.FindAsync(id);
    if (user == null)
    {
      return NotFound();
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
}
