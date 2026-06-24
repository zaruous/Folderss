using System;

namespace Folderss.Models
{
    public class OpenWithEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ExecutablePath { get; set; }
        // Use {0} as placeholder for the target path(s). Multiple paths are space-separated and quoted.
        public string Arguments { get; set; }
        // "*" = all files and folders, "folder" = directories only, ".txt,.cs" = specific extensions
        public string ExtensionMask { get; set; }

        public OpenWithEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Description = "";
            ExecutablePath = "";
            Arguments = "\"{0}\"";
            ExtensionMask = "*";
        }

        public OpenWithEntry Clone()
        {
            return new OpenWithEntry
            {
                Id = Id,
                Name = Name,
                Description = Description,
                ExecutablePath = ExecutablePath,
                Arguments = Arguments,
                ExtensionMask = ExtensionMask
            };
        }
    }
}
