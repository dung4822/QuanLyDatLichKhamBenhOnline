using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly AppDbContext _context;

    public ChatRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddSessionAsync(ChatSession session) => await _context.ChatSessions.AddAsync(session);

    public async Task<ChatSession?> GetSessionAsync(Guid sessionId, int patientId, bool tracking = false)
    {
        var query = tracking ? _context.ChatSessions : _context.ChatSessions.AsNoTracking();
        return await query.FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.PatientId == patientId);
    }

    public async Task<List<ChatSession>> GetSessionsAsync(int patientId, int take)
        => await _context.ChatSessions
            .AsNoTracking()
            .Where(x => x.PatientId == patientId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take)
            .ToListAsync();

    public async Task<List<ChatTurn>> GetRecentTurnsAsync(Guid sessionId, int take)
    {
        var turns = await _context.ChatTurns
            .AsNoTracking()
            .Include(x => x.Action)
            .Where(x => x.ChatSessionId == sessionId)
            .OrderByDescending(x => x.ChatTurnId)
            .Take(take)
            .ToListAsync();
        turns.Reverse();
        return turns;
    }

    public async Task<ChatTurn?> GetTurnByClientMessageIdAsync(Guid sessionId, Guid clientMessageId)
        => await _context.ChatTurns
            .AsNoTracking()
            .Include(x => x.Action)
            .FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.ClientMessageId == clientMessageId);

    public async Task<ChatAction?> GetActionAsync(Guid actionId, int patientId, bool tracking = false)
    {
        var query = tracking ? _context.ChatActions : _context.ChatActions.AsNoTracking();
        return await query.FirstOrDefaultAsync(x =>
            x.ChatActionId == actionId && x.ChatTurn.ChatSession.PatientId == patientId);
    }

    public async Task<bool> TryCompleteAsync(Guid actionId, ChatActionStatus status, string? failureReason = null)
    {
        var updated = await _context.ChatActions
            .Where(x => x.ChatActionId == actionId && x.Status == ChatActionStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.FailureReason, failureReason)
                .SetProperty(x => x.CompletedAt, DateTime.UtcNow));
        return updated == 1;
    }

    public async Task SupersedePendingAsync(Guid sessionId, Guid keepActionId)
        => await _context.ChatActions
            .Where(x => x.ChatTurn.ChatSessionId == sessionId && x.Status == ChatActionStatus.Pending &&
                x.ChatActionId != keepActionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ChatActionStatus.Superseded)
                .SetProperty(x => x.CompletedAt, DateTime.UtcNow));

    public async Task<bool> TryAddTurnAsync(ChatTurn turn)
    {
        await _context.ChatTurns.AddAsync(turn);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ChatTurns_ChatSessionId_ClientMessageId" })
        {
            // Request trùng đã lưu trước; bỏ các thay đổi của request này.
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
