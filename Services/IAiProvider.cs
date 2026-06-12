using System.Threading.Tasks;
using NewsImpactRanker.WinForms.Models;

namespace NewsImpactRanker.WinForms.Services
{
    public interface IAiProvider
    {
        string Name { get; }
        Task<ServiceResult<TopicScoresResponse>> ClassifyAsync(string text, string prompt);
    }
}
