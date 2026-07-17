using Folderss.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Folderss.Services
{
    public static class DiskUsageService
    {
        public static List<DriveUsageInfo> GetDriveUsage()
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new DriveUsageInfo
                {
                    Name = drive.Name,
                    DisplayName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name
                        : string.Format("{0} ({1})", drive.Name.TrimEnd('\\'), drive.VolumeLabel),
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace
                })
                .ToList();
        }
    }
}
