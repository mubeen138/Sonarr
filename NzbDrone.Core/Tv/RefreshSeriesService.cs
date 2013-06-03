﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class RefreshSeriesService : IExecute<RefreshSeriesCommand>, IHandleAsync<SeriesAddedEvent>
    {
        private readonly IProvideSeriesInfo _seriesInfo;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly ISeasonRepository _seasonRepository;
        private readonly IMessageAggregator _messageAggregator;
        private readonly Logger _logger;

        public RefreshSeriesService(IProvideSeriesInfo seriesInfo, ISeriesService seriesService, IEpisodeService episodeService,
            ISeasonRepository seasonRepository, IMessageAggregator messageAggregator, Logger logger)
        {
            _seriesInfo = seriesInfo;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _seasonRepository = seasonRepository;
            _messageAggregator = messageAggregator;
            _logger = logger;
        }


        public void Execute(RefreshSeriesCommand message)
        {
            if (message.SeriesId.HasValue)
            {
                RefreshSeriesInfo(message.SeriesId.Value);
            }
            else
            {
                var ids = _seriesService.GetAllSeries().OrderBy(c => c.LastInfoSync).Select(c => c.Id).ToList();

                foreach (var id in ids)
                {
                    RefreshSeriesInfo(id);
                }
            }

        }

        public void HandleAsync(SeriesAddedEvent message)
        {
            RefreshSeriesInfo(message.Series.Id);
        }

        private Series RefreshSeriesInfo(int seriesId)
        {
            var series = _seriesService.GetSeries(seriesId);
            var tuple = _seriesInfo.GetSeriesInfo(series.TvdbId);

            var seriesInfo = tuple.Item1;

            series.Title = seriesInfo.Title;
            series.AirTime = seriesInfo.AirTime;
            series.Overview = seriesInfo.Overview;
            series.Status = seriesInfo.Status;
            series.CleanTitle = Parser.Parser.NormalizeTitle(seriesInfo.Title);
            series.LastInfoSync = DateTime.Now;
            series.Runtime = seriesInfo.Runtime;
            series.Images = seriesInfo.Images;
            series.Network = seriesInfo.Network;
            series.FirstAired = seriesInfo.FirstAired;
            _seriesService.UpdateSeries(series);

            //Todo: We need to get the UtcOffset from TVRage, since its not available from trakt

            RefreshEpisodeInfo(series, tuple.Item2);

            _messageAggregator.PublishEvent(new SeriesUpdatedEvent(series));
            return series;
        }

        private void RefreshEpisodeInfo(Series series, List<Episode> remoteEpisodes)
        {
            _logger.Trace("Starting episode info refresh for series: {0}", series.Title.WithDefault(series.Id));
            var successCount = 0;
            var failCount = 0;


            var seriesEpisodes = _episodeService.GetEpisodeBySeries(series.Id);

            var seasons = _seasonRepository.GetSeasonBySeries(series.Id);

            var updateList = new List<Episode>();
            var newList = new List<Episode>();

            foreach (var episode in remoteEpisodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
            {
                try
                {
                    _logger.Trace("Updating info for [{0}] - S{1:00}E{2:00}", series.Title, episode.SeasonNumber, episode.EpisodeNumber);

                    var episodeToUpdate = seriesEpisodes.SingleOrDefault(e =>
                        e.TvDbEpisodeId == episode.TvDbEpisodeId ||
                        (e.SeasonNumber == episode.SeasonNumber && e.EpisodeNumber == episode.EpisodeNumber));

                    if (episodeToUpdate == null)
                    {
                        episodeToUpdate = new Episode();
                        newList.Add(episodeToUpdate);

                        //If it is Episode Zero Ignore it (specials, sneak peeks.)
                        if (episode.EpisodeNumber == 0 && episode.SeasonNumber != 1)
                        {
                            episodeToUpdate.Ignored = true;
                        }
                        else
                        {
                            var season = seasons.FirstOrDefault(c => c.SeasonNumber == episode.SeasonNumber);
                            episodeToUpdate.Ignored = season != null && season.Ignored;
                        }
                    }
                    else
                    {
                        updateList.Add(episodeToUpdate);
                    }

                    if ((episodeToUpdate.EpisodeNumber != episode.EpisodeNumber ||
                         episodeToUpdate.SeasonNumber != episode.SeasonNumber) &&
                        episodeToUpdate.EpisodeFileId > 0)
                    {
                        _logger.Debug("Un-linking episode file because the episode number has changed");
                        episodeToUpdate.EpisodeFileId = 0;
                    }

                    episodeToUpdate.SeriesId = series.Id;
                    episodeToUpdate.TvDbEpisodeId = episode.TvDbEpisodeId;
                    episodeToUpdate.EpisodeNumber = episode.EpisodeNumber;
                    episodeToUpdate.SeasonNumber = episode.SeasonNumber;
                    episodeToUpdate.Title = episode.Title;
                    episodeToUpdate.Overview = episode.Overview;
                    episodeToUpdate.AirDate = episode.AirDate;

                    successCount++;
                }
                catch (Exception e)
                {
                    _logger.FatalException(String.Format("An error has occurred while updating episode info for series {0}", series.Title), e);
                    failCount++;
                }
            }

            var allEpisodes = new List<Episode>();
            allEpisodes.AddRange(newList);
            allEpisodes.AddRange(updateList);

            var groups = allEpisodes.GroupBy(e => new { e.SeriesId, e.AirDate }).Where(g => g.Count() > 1).ToList();

            foreach (var group in groups)
            {
                int episodeCount = 0;
                foreach (var episode in group.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
                {
                    episode.AirDate = episode.AirDate.Value.AddMinutes(series.Runtime * episodeCount);
                    episodeCount++;
                }
            }

            _episodeService.InsertMany(newList);
            _episodeService.UpdateMany(updateList);

            if (newList.Any())
            {
                _messageAggregator.PublishEvent(new EpisodeInfoAddedEvent(newList, series));
            }

            if (updateList.Any())
            {
                _messageAggregator.PublishEvent(new EpisodeInfoUpdatedEvent(updateList));
            }

            if (failCount != 0)
            {
                _logger.Info("Finished episode refresh for series: {0}. Successful: {1} - Failed: {2} ",
                            series.Title, successCount, failCount);
            }
            else
            {
                _logger.Info("Finished episode refresh for series: {0}.", series.Title);
            }

            //DeleteEpisodesNotAvailableAnymore(series, remoteEpisodes);
        }


        /*        private void DeleteEpisodesNotAvailableAnymore(Series series, IEnumerable<Episode> onlineEpisodes)
                {
                    //Todo: This will not work as currently implemented - what are we trying to do here?
                    return;
                    _logger.Trace("Starting deletion of episodes that no longer exist in TVDB: {0}", series.Title.WithDefault(series.Id));
                    foreach (var episode in onlineEpisodes)
                    {
                        _episodeRepository.Delete(episode.Id);
                    }

                    _logger.Trace("Deleted episodes that no longer exist in TVDB for {0}", series.Id);
                }*/
    }

    public class RefreshSeriesCommand : ICommand
    {
        public int? SeriesId { get; private set; }

        public RefreshSeriesCommand(int? seriesId)
        {
            SeriesId = seriesId;
        }
    }
}