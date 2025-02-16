using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    public class MatchesGrabSpecification : IImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MatchesGrabSpecification(Logger logger)
        {
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (localEpisode.ExistingFile)
            {
                return ImportSpecDecision.Accept();
            }

            var releaseInfo = localEpisode.Release;

            if (releaseInfo == null || releaseInfo.EpisodeIds.Empty())
            {
                return ImportSpecDecision.Accept();
            }

            var unexpected = localEpisode.Episodes.Where(e => releaseInfo.EpisodeIds.All(o => o != e.Id)).ToList();

            if (unexpected.Any())
            {
                _logger.Debug("Unexpected episode(s) in file: {0}", FormatEpisode(unexpected));

                if (unexpected.Count == 1)
                {
                    return ImportSpecDecision.Reject(ImportRejectionReason.EpisodeNotFoundInRelease, "Episode {0} was not found in the grabbed release: {1}", FormatEpisode(unexpected), releaseInfo.Title);
                }

                return ImportSpecDecision.Reject(ImportRejectionReason.EpisodeNotFoundInRelease, "Episodes {0} were not found in the grabbed release: {1}", FormatEpisode(unexpected), releaseInfo.Title);
            }

            return ImportSpecDecision.Accept();
        }

        private string FormatEpisode(List<Episode> episodes)
        {
            return string.Join(", ", episodes.Select(e => $"{e.SeasonNumber}x{e.EpisodeNumber:00}"));
        }
    }
}
