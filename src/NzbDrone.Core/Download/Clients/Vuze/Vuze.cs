using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Transmission;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Vuze
{
    public class Vuze : TransmissionBase
    {
        private const int MINIMUM_SUPPORTED_PROTOCOL_VERSION = 14;

        public override string Name => "Vuze";
        public override bool SupportsLabels => false;

        public Vuze(ITransmissionProxy proxy,
                    ITorrentFileInfoReader torrentFileInfoReader,
                    IHttpClient httpClient,
                    IConfigService configService,
                    IDiskProvider diskProvider,
                    IRemotePathMappingService remotePathMappingService,
                    ILocalizationService localizationService,
                    IBlocklistService blocklistService,
                    Logger logger)
            : base(proxy, torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, localizationService, blocklistService, logger)
        {
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            _proxy.RemoveTorrent(item.DownloadId, deleteData, Settings);
        }

        protected override OsPath GetOutputPath(OsPath outputPath, TransmissionTorrent torrent)
        {
            // Vuze has similar behavior as uTorrent:
            // - A multi-file torrent is downloaded in a job folder and 'outputPath' points to that directory directly.
            // - A single-file torrent is downloaded in the root folder and 'outputPath' poinst to that root folder.
            // We have to make sure the return value points to the job folder OR file.
            if (outputPath.FileName == torrent.Name || torrent.FileCount > 1)
            {
                _logger.Trace("Vuze output directory: {0}", outputPath);
            }
            else
            {
                outputPath = outputPath + torrent.Name;
                _logger.Trace("Vuze output file: {0}", outputPath);
            }

            return outputPath;
        }

        protected override ValidationFailure ValidateVersion()
        {
            var versionString = _proxy.GetProtocolVersion(Settings);

            _logger.Debug("Vuze protocol version information: {0}", versionString);

            if (!int.TryParse(versionString, out var version) || version < MINIMUM_SUPPORTED_PROTOCOL_VERSION)
            {
                {
                    return new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("DownloadClientVuzeValidationErrorVersion"));
                }
            }

            return null;
        }
    }
}
