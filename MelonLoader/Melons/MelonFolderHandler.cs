﻿using MelonLoader.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader.Logging;

namespace MelonLoader.Melons;

public static class MelonFolderHandler
{
    private static bool _firstSpacer;
    private static List<string> _userLibDirs = [];
    private static List<string> _pluginDirs = [];
    private static List<string> _modDirs = [];
    private static List<string> _fullPathExclusions = [];

    public enum eNameExclusionType
    {
        EXACT_MATCH,
        STARTS_WITH,
        ENDS_WITH
    }
    private static List<LemonTuple<eNameExclusionType, string>> _nameExclusions = [
        new(eNameExclusionType.STARTS_WITH, "~"),
        new(eNameExclusionType.STARTS_WITH, "."),

        new(eNameExclusionType.EXACT_MATCH, "Disabled"),
        new(eNameExclusionType.EXACT_MATCH, "Broken"),
    ];

    internal enum ScanType
    {
        UserLibs,
        Plugins,
        Mods
    }

    public static void AddNameExclusion(eNameExclusionType type, string param)
    {
        if (string.IsNullOrEmpty(param)
#if NET6_0_OR_GREATER
            || string.IsNullOrWhiteSpace(param)
#endif
            )
            throw new ArgumentNullException(nameof(param));

        _nameExclusions.Add(new(type, param));
    }

    public static void AddFullPathExclusion(string path)
    {
        if (string.IsNullOrEmpty(path)
#if NET6_0_OR_GREATER
            || string.IsNullOrWhiteSpace(path)
#endif
            )
            throw new ArgumentNullException(nameof(path));

        _fullPathExclusions.Add(path);
    }

    internal static void ScanForFolders()
    {
        // Add Base Directories to Start of List
        _userLibDirs.Add(MelonEnvironment.UserLibsDirectory);
        _pluginDirs.Add(MelonEnvironment.PluginsDirectory);
        _modDirs.Add(MelonEnvironment.ModsDirectory);

        // Scan Base Folders
        FindSubFolders(ScanType.UserLibs, MelonEnvironment.UserLibsDirectory, true, ref _userLibDirs, ref _pluginDirs, ref _modDirs);
        FindSubFolders(ScanType.Plugins, MelonEnvironment.PluginsDirectory, true, ref _userLibDirs, ref _pluginDirs, ref _modDirs);
        FindSubFolders(ScanType.Mods, MelonEnvironment.ModsDirectory, true, ref _userLibDirs, ref _pluginDirs, ref _modDirs);

        // Add Directories to Resolver
        foreach (string directory in _userLibDirs)
        {
            MelonUtils.AddNativeDLLDirectory(directory);
            Resolver.MelonAssemblyResolver.AddSearchDirectory(directory);
        }
        foreach (string directory in _pluginDirs)
            Resolver.MelonAssemblyResolver.AddSearchDirectory(directory);
        foreach (string directory in _modDirs)
            Resolver.MelonAssemblyResolver.AddSearchDirectory(directory);
    }

    internal static void LoadMelons(ScanType type)
    {
        LogStart(type);

        bool hasWroteLine = false;
        List<MelonAssembly> melonAssemblies = new();

        if (type == ScanType.UserLibs)
            MelonPreprocessor.LoadFolders(_userLibDirs, false, ref hasWroteLine, ref melonAssemblies);

        int count = melonAssemblies.Count;
        string typeName = "UserLib".MakePlural(count);

        if (type == ScanType.Plugins)
        {
            MelonPreprocessor.LoadFolders(_pluginDirs, true, ref hasWroteLine, ref melonAssemblies);
            List<MelonPlugin> pluginsLoaded = LoadMelons<MelonPlugin>(melonAssemblies);
            if (hasWroteLine)
                MelonLogger.WriteSpacer();
            MelonBase.RegisterSorted(pluginsLoaded);

            count = MelonPlugin.RegisteredMelons.Count;
            typeName = MelonPlugin.TypeName.MakePlural(count);
        }

        if (type == ScanType.Mods)
        {
            MelonPreprocessor.LoadFolders(_modDirs, true, ref hasWroteLine, ref melonAssemblies);
            List<MelonMod> modsLoaded = LoadMelons<MelonMod>(melonAssemblies);
            if (hasWroteLine)
                MelonLogger.WriteSpacer();
            MelonBase.RegisterSorted(modsLoaded);

            count = MelonMod.RegisteredMelons.Count;
            typeName = MelonMod.TypeName.MakePlural(count);
        }

        if (hasWroteLine)
            MelonLogger.WriteLine(ColorARGB.Magenta);
        MelonLogger.Msg($"{count} {typeName} loaded.");

        if (_firstSpacer || (type == ScanType.Mods))
            MelonLogger.WriteSpacer();
        _firstSpacer = true;
    }

    private static void LogStart(ScanType type)
    {
        string typeName = Enum.GetName(typeof(ScanType), type);
        var loadingMsg = $"Loading {typeName}...";
        MelonLogger.WriteSpacer();
        MelonLogger.Msg(loadingMsg);
    }

    private static bool IsNameExcluded(LemonTuple<eNameExclusionType, string> tuple,
        string name,
        string nameLower)
        => tuple.Item1 switch
        {
            eNameExclusionType.EXACT_MATCH => ((tuple.Item2 == name) || (tuple.Item2 == nameLower)),
            eNameExclusionType.STARTS_WITH => (name.StartsWith(tuple.Item2) || nameLower.StartsWith(tuple.Item2)),
            eNameExclusionType.ENDS_WITH => (name.EndsWith(tuple.Item2) || nameLower.EndsWith(tuple.Item2)),
            _ => false,
        };

    private static bool IsExcluded(string path,
        string name)
    {
        foreach (var exclusion in _fullPathExclusions)
            if (exclusion == path)
                return true;

        string nameLower = name.ToLower();
        foreach (var tuple in _nameExclusions)
            if (IsNameExcluded(tuple, name, nameLower))
                return true;

        return false;
    }

    private static List<T> LoadMelons<T>(List<MelonAssembly> melonAssemblies)
        where T : MelonTypeBase<T>
    {
        // Load Melons from Assembly
        foreach (var asm in melonAssemblies)
            asm.LoadMelons();

        List<T> loadedMelons = new();
        foreach (var asm in melonAssemblies)
        foreach (var m in asm.LoadedMelons)
        {
            // Validate Type
            if (m is T t)
            {
                loadedMelons.Add(t);
                continue;
            }

            // Log Failure
            MelonLogger.Warning($"Failed to load Melon '{m.Info.Name}' from '{m.MelonAssembly.Location}': The given Melon is a {m.MelonTypeName} and cannot be loaded as a {MelonTypeBase<T>.TypeName}. Make sure it's in the right folder.");
        }
        return loadedMelons;
    }

    private static void AddFolder(ScanType scanType,
        string path,
        string directoryName,
        bool require_manifest,
        ref List<string> userLibDirectories,
        ref List<string> pluginDirectories,
        ref List<string> modDirectories)
    {
        List<string> dirList = directoryName switch
        {
            // First check for Name Identifier
            "UserLibs" => userLibDirectories,
            "Plugins" => pluginDirectories,
            "Mods" => modDirectories,

            // Last check for ScanType
            _ => scanType switch
            {
                ScanType.UserLibs => userLibDirectories,
                ScanType.Plugins => pluginDirectories,
                ScanType.Mods => modDirectories,
                _ => throw new ArgumentOutOfRangeException(nameof(scanType), scanType, null)
            }
        };
        dirList.Add(path);
        FindSubFolders(scanType, path, require_manifest, ref userLibDirectories, ref pluginDirectories, ref modDirectories);
    }

    private static void FindSubFolders(ScanType scanType,
        string path,
        bool require_manifest,
        ref List<string> userLibDirectories,
        ref List<string> pluginDirectories,
        ref List<string> modDirectories)
    {
        if (!LoaderConfig.Current.Loader.DisableSubFolderLoad)
            return;

        // Get Directories
        string[] directories = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
        if (directories.Length <= 0)
            return;

        // Parse Directories
        foreach (var directoryPath in directories)
        {
            // Validate Path
            if (!Directory.Exists(directoryPath))
                continue;

            string directoryName = Path.GetFileName(directoryPath);
            if (IsExcluded(directoryPath, directoryName))
                continue;

            if (require_manifest 
                && !LoaderConfig.Current.Loader.DisableSubFolderManifest)
            {
                string manifestFilePath = Path.Combine(directoryPath, "manifest.json");
                if (!File.Exists(manifestFilePath))
                    continue;
            }

            AddFolder(scanType, directoryPath, directoryName, false, ref userLibDirectories, ref pluginDirectories, ref modDirectories);
        }
    }
}