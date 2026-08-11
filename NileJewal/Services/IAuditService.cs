namespace NileJewal.Services
{
    public interface IAuditService
    {
        Task LogAsync(string userId, string userName, string action, string entityName, int entityId, string details);
    }
}