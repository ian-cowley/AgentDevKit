using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Glacier.AgentDevKit.Adk;

public class AgentDbContext : DbContext
{
    public DbSet<DbSession> Sessions { get; set; } = null!;
    public DbSet<DbMessage> Messages { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=agent_sessions.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbMessage>()
            .HasOne<DbSession>()
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId);
    }
}

public class DbSession
{
    public string Id { get; set; } = string.Empty;
    public List<DbMessage> Messages { get; set; } = new();
}

public class DbMessage
{
    public int Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ContentJson { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SqliteSessionProvider : ISessionProvider
{
    public async Task<List<LlmContent>> GetHistoryAsync(string sessionId)
    {
        using var db = new AgentDbContext();
        await db.Database.EnsureCreatedAsync();

        var session = await db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return new List<LlmContent>();

        return session.Messages
            .OrderBy(m => m.Timestamp)
            .Select(m => JsonSerializer.Deserialize<LlmContent>(m.ContentJson)!)
            .ToList();
    }

    public async Task SaveMessageAsync(string sessionId, LlmContent message)
    {
        using var db = new AgentDbContext();
        await db.Database.EnsureCreatedAsync();

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null)
        {
            session = new DbSession { Id = sessionId };
            db.Sessions.Add(session);
        }

        db.Messages.Add(new DbMessage
        {
            SessionId = sessionId,
            Role = message.Role ?? "user",
            ContentJson = JsonSerializer.Serialize(message)
        });

        await db.SaveChangesAsync();
    }
}
