﻿using CriFs.V2.Hook.Interfaces;
using CriFsV2Lib.Definitions.Utilities;
using FileEmulationFramework.Lib.Utilities;
using Persona.Merger.Cache;
using Persona.Merger.Patching.Tbl;
using Persona.Merger.Patching.Tbl.FieldResolvers.P5R;
using Persona.Merger.Patching.Tbl.FieldResolvers.Generic;
using Persona.Merger.Patching.Tbl.FieldResolvers.P5R.Name;
using Reloaded.Universal.Localisation.Framework.Interfaces;
using static p5rpc.modloader.Merging.MergeUtils;

namespace p5rpc.modloader.Merging.Tbl;

internal class P5RTblMerger : IFileMerger
{
    private readonly ICriFsRedirectorApi _criFsApi;
    private readonly Logger _logger;
    private readonly MergedFileCache _mergedFileCache;
    private readonly MergeUtils _utils;
    private readonly ILocalisationFramework _localisationFramework;

    internal P5RTblMerger(MergeUtils utils, Logger logger, MergedFileCache mergedFileCache,
        ICriFsRedirectorApi criFsApi, ILocalisationFramework localisationFramework)
    {
        _utils = utils;
        _logger = logger;
        _mergedFileCache = mergedFileCache;
        _criFsApi = criFsApi;
        _localisationFramework = localisationFramework;
    }

    public void Merge(string[] cpks, ICriFsRedirectorApi.BindContext context)
    {
        // Note: Actual merging logic is optimised but code in mod could use some more work.
        var pathToFileMap = context.RelativePathToFileMap;
        var tasks = new List<ValueTask>
        {
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\SKILL.TBL", TblType.Skill, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\ELSAI.TBL", TblType.Elsai, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\ITEM.TBL", TblType.Item, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\EXIST.TBL", TblType.Exist, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\PLAYER.TBL", TblType.Player, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\ENCOUNT.TBL", TblType.Encount, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\PERSONA.TBL", TblType.Persona, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\AICALC.TBL", TblType.AiCalc, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\VISUAL.TBL", TblType.Visual, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\NAME.TBL", TblType.Name, cpks),
            PatchTbl(pathToFileMap, @"R2\BATTLE\TABLE\UNIT.TBL", TblType.Unit, cpks),
            PatchAnyFile(pathToFileMap, @"R2\CALENDAR\CLDWEATHER.BIN", 1, cpks),
            PatchAnyFile(pathToFileMap, @"R2\RESOURCE\RESRCNPCEXIST.BIN", 1, cpks),
            PatchAnyFile(pathToFileMap, @"R2\EVENT\EVTDATEOFFTABLE.BIN", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\EVENT\EVTDATETABLE.BIN", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\EVENT\EVTDDDECOTABLE.BIN", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\EVENT\EVTFADEOUTTABLE.BIN", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\INIT\PMCHATINVITE365TBL.DAT", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\RESOURCE\RESRCNPCTBL.BIN", 2, cpks),
            PatchAnyFile(pathToFileMap, @"R2\BUSTUP\DATA\BUSTUP_PARAM.DAT", 4, cpks),
            PatchAnyFile(pathToFileMap, @"R2\FONT\ASSIST\MSGASSISTBUSTUPPARAM.DAT", 4, cpks),
            PatchAnyFile(pathToFileMap, @"R2\INIT\SHDPERSONA.PDD", 4, cpks),
            PatchAnyFile(pathToFileMap, @"R2\INIT\SHDPERSONAENEMY.PDD", 4, cpks)
        };

        Task.WhenAll(tasks.Select(x => x.AsTask())).Wait();
    }

    private async ValueTask PatchTbl(Dictionary<string, List<ICriFsRedirectorApi.BindFileInfo>> pathToFileMap,
        string tblPath, TblType type, string[] cpks)
    {
        if (!pathToFileMap.TryGetValue(tblPath, out var candidates))
            return;

        var pathInCpk = RemoveR2Prefix(tblPath);
        if (!_utils.TryFindFileInAnyCpk(pathInCpk, cpks, out var cpkPath, out var cpkEntry, out var fileIndex))
        {
            _logger.Warning("Unable to find TBL in any CPK {0}", pathInCpk);
            return;
        }

        // Build cache key
        var cacheKey = GetCacheKeyAndSources(tblPath, candidates, out var sources);
        if (_mergedFileCache.TryGet(cacheKey, sources, out var cachedFilePath))
        {
            _logger.Info("Loading Merged TBL {0} from Cache ({1})", tblPath, cachedFilePath);
            _utils.ReplaceFileInBinderInput(pathToFileMap, tblPath, cachedFilePath);
            return;
        }

        // Else Merge our Data
        // First we extract.
        await Task.Run(async () =>
        {
            _logger.Info("Merging {0} with key {1}.", tblPath, cacheKey);
            await using var cpkStream =
                new FileStream(cpkPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var reader = _criFsApi.GetCriFsLib().CreateCpkReader(cpkStream, false);
            using var extractedTable = reader.ExtractFile(cpkEntry.Files[fileIndex].File);

            // Then we merge
            byte[] patched;
            // Note: https://github.com/Sewer56/p5rpc.modloader/pull/41#issue-2939748981
            // Async reads don't work here.
            if (type != TblType.Name)
                patched = PatchTable(type, extractedTable, candidates);
            else
                patched = PatchNameTable(extractedTable, candidates);

            // Then we store in cache.
            var item = await _mergedFileCache.AddAsync(cacheKey, sources, patched);
            _utils.ReplaceFileInBinderInput(pathToFileMap, tblPath,
                Path.Combine(_mergedFileCache.CacheFolder, item.RelativePath));
            _logger.Info("Merge {0} Complete. Cached to {1}.", tblPath, item.RelativePath);
        });
    }

    private byte[] PatchNameTable(ArrayRental extractedTable,
        List<ICriFsRedirectorApi.BindFileInfo> candidates)
    {
        // Name tbls of different languages cannot be merged, ensure only those of the same language are used
        if (_localisationFramework.TryGetLanguage(out var language) && language != Language.English)
            candidates = candidates.Where(x => _localisationFramework.IsFileLocalised(x.FullPath)).ToList();

        var table = ParsedNameTable.ParseTable(extractedTable.RawArray);
        var otherTables = new ParsedNameTable[candidates.Count];
        for (var x = 0; x < otherTables.Length; x++)
            otherTables[x] = ParsedNameTable.ParseTable(File.ReadAllBytes(candidates[x].FullPath));

        var diff = NameTableMerger.CreateDiffs(table, otherTables);
        return NameTableMerger.Merge(table, diff).ToArray();
    }

    private static byte[] PatchTable(TblType type, ArrayRental extractedTable,
        List<ICriFsRedirectorApi.BindFileInfo> candidates)
    {
        var patcher = new P5RTblPatcher(extractedTable.Span.ToArray(), type);
        var patches = new List<TblPatch>(candidates.Count);
        for (var x = 0; x < candidates.Count; x++)
            patches.Add(patcher.GeneratePatch(File.ReadAllBytes(candidates[x].FullPath)));

        return patcher.Apply(patches);
    }

    private async ValueTask PatchAnyFile(Dictionary<string, List<ICriFsRedirectorApi.BindFileInfo>> pathToFileMap,
        string tblPath, int ResolverSize, string[] cpks)
    {
        if (!pathToFileMap.TryGetValue(tblPath, out var candidates))
            return;

        var pathInCpk = RemoveR2Prefix(tblPath);
        if (!_utils.TryFindFileInAnyCpk(pathInCpk, cpks, out var cpkPath, out var cpkEntry, out var fileIndex))
        {
            _logger.Warning("Unable to find TBL in any CPK {0}", pathInCpk);
            return;
        }

        // Build cache key
        var cacheKey = GetCacheKeyAndSources(tblPath, candidates, out var sources);
        if (_mergedFileCache.TryGet(cacheKey, sources, out var cachedFilePath))
        {
            _logger.Info("Loading Merged TBL {0} from Cache ({1})", tblPath, cachedFilePath);
            _utils.ReplaceFileInBinderInput(pathToFileMap, tblPath, cachedFilePath);
            return;
        }

        // Else Merge our Data
        // First we extract.
        await Task.Run(async () =>
        {
            _logger.Info("Merging {0} with key {1}.", tblPath, cacheKey);
            await using var cpkStream =
                new FileStream(cpkPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var reader = _criFsApi.GetCriFsLib().CreateCpkReader(cpkStream, false);
            using var extractedTable = reader.ExtractFile(cpkEntry.Files[fileIndex].File);

            // Then we merge
            var patched = await PatchAny(extractedTable, candidates, ResolverSize);

            // Then we store in cache.
            var item = await _mergedFileCache.AddAsync(cacheKey, sources, patched);
            _utils.ReplaceFileInBinderInput(pathToFileMap, tblPath,
                Path.Combine(_mergedFileCache.CacheFolder, item.RelativePath));
            _logger.Info("Merge {0} Complete. Cached to {1}.", tblPath, item.RelativePath);
        });
    }

    private static async Task<byte[]> PatchAny(ArrayRental extractedTable,
        List<ICriFsRedirectorApi.BindFileInfo> candidates, int ResolverSize)
    {
        var patcher = new GenericPatcher(extractedTable.Span.ToArray());
        var patches = new List<TblPatch>(candidates.Count);
        for (var x = 0; x < candidates.Count; x++)
            patches.Add(patcher.GeneratePatchGeneric(await File.ReadAllBytesAsync(candidates[x].FullPath), ResolverSize));

        return patcher.ApplyGeneric(patches);
    }
}