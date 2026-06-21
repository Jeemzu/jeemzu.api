using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IScoreService
{
    Task<ScoreResponse> SaveScoreAsync(SubmitScoreRequest request, string username);
    Task<IEnumerable<ScoreResponse>> GetLeaderboardAsync(string gameId, int limit);
    Task<GameSummaryResponse> GetGameSummaryAsync(string gameId, Guid? userId);
}
