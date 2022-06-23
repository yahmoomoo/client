﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Managers;
using MareSynchronos.Models;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;

namespace MareSynchronos.Factories
{
    public class CharacterCacheFactory
    {
        private readonly ClientState _clientState;
        private readonly IpcManager _ipcManager;
        private readonly FileReplacementFactory _factory;

        public CharacterCacheFactory(ClientState clientState, IpcManager ipcManager, FileReplacementFactory factory)
        {
            _clientState = clientState;
            _ipcManager = ipcManager;
            _factory = factory;
        }

        private string GetPlayerName()
        {
            return _clientState.LocalPlayer!.Name.ToString();
        }

        public unsafe CharacterCache BuildCharacterCache()
        {
            var cache = new CharacterCache();

            while (_clientState.LocalPlayer == null)
            {
                PluginLog.Debug("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }
            var model = (CharacterBase*)((Character*)_clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();
            for (var idx = 0; idx < model->SlotCount; ++idx)
            {
                var mdl = (RenderModel*)model->ModelArray[idx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement cachedMdlResource = _factory.Create();
                cachedMdlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mdlPath, GetPlayerName());
                cachedMdlResource.SetResolvedPath(mdlPath);
                //PluginLog.Verbose("Resolving for model " + mdlPath);

                cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    //var mtrlFileResource = factory.Create();
                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];
                    //PluginLog.Verbose("Resolving for material " + mtrlPath);
                    var cachedMtrlResource = _factory.Create();
                    cachedMtrlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mtrlPath, GetPlayerName());
                    cachedMtrlResource.SetResolvedPath(mtrlPath);
                    cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResource->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath.ToString())) continue;

                        var cachedTexResource = _factory.Create();
                        cachedTexResource.GamePaths = new[] { texPath };
                        cachedTexResource.SetResolvedPath(_ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!);
                        if (!cachedTexResource.HasFileReplacement)
                        {
                            // try resolving tex with -- in name instead
                            texPath = texPath.Insert(texPath.LastIndexOf('/') + 1, "--");
                            var reResolvedPath = _ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!;
                            if (reResolvedPath != texPath)
                            {
                                cachedTexResource.GamePaths = new[] { texPath };
                                cachedTexResource.SetResolvedPath(reResolvedPath);
                            }
                        }
                        cache.AddAssociatedResource(cachedTexResource, cachedMdlResource, cachedMtrlResource);
                    }
                }
            }

            return cache;
        }
    }
}
