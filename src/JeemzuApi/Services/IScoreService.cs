using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IScoreService
{
    Task<ScoreResponse> SaveScoreAsync(SubmitScoreRequest request);
    Task<IEnumerable<ScoreResponse>> GetLeaderboardAsync(string gameId, int limit);
}
