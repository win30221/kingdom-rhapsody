using System;
using System.IO;
using UnityEngine;

namespace KingdomRhapsody.Editor
{
    internal static class CharacterArtSource
    {
        internal static string Resolve(string relativePath)
        {
            string art = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art"));
            string path = Path.GetFullPath(Path.Combine(art, relativePath));
            if (!path.StartsWith(art + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
                throw new FileNotFoundException("Could not find " + relativePath + " in the repository art folder.", path);
            return path;
        }
    }
}
