using System;

namespace Folderss.Models
{
    public sealed class DriveUsageInfo
    {
        private const double BytesPerGigabyte = 1024.0 * 1024.0 * 1024.0;

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public long TotalBytes { get; set; }

        public long FreeBytes { get; set; }

        public long UsedBytes
        {
            get { return Math.Max(0, TotalBytes - FreeBytes); }
        }

        public double TotalGB
        {
            get { return TotalBytes / BytesPerGigabyte; }
        }

        public double UsedGB
        {
            get { return UsedBytes / BytesPerGigabyte; }
        }

        public double FreeGB
        {
            get { return FreeBytes / BytesPerGigabyte; }
        }

        public double UsedFraction
        {
            get { return TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes; }
        }

        public double FreeFraction
        {
            get { return 1 - UsedFraction; }
        }
    }
}
