/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.World.Terrain;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.World.Archiver
{
    /// <summary>
    /// Prepare to write out an archive.
    /// </summary>
    public class ArchiveWriteRequestPreparation
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected Scene m_scene;
        protected Stream m_saveStream;

        /// <summary>
        /// Used as a temporary store of an asset which represents an object.  This can be a null if no appropriate
        /// asset was found by the asset service.
        /// </summary>
        protected AssetBase m_requestedObjectAsset;

        /// <summary>
        /// Signal whether we are currently waiting for the asset service to deliver an asset.
        /// </summary>
        protected bool m_waitingForObjectAsset;

        /// <summary>
        /// Constructor
        /// </summary>
        public ArchiveWriteRequestPreparation(Scene scene, string savePath)
        {
            m_scene = scene;
            m_saveStream = new GZipStream(new FileStream(savePath, FileMode.Create), CompressionMode.Compress);            
        }
        
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="saveStream">The stream to which to save data.</param>
        public ArchiveWriteRequestPreparation(Scene scene, Stream saveStream)
        {
            m_scene = scene;
            m_saveStream = saveStream;
        }        

        /// <summary>
        /// The callback made when we request the asset for an object from the asset service.
        /// </summary>
        public void AssetRequestCallback(UUID assetID, AssetBase asset)
        {
            lock (this)
            {
                m_requestedObjectAsset = asset;
                m_waitingForObjectAsset = false;
                Monitor.Pulse(this);
            }
        }

        /// <summary>
        /// Get an asset synchronously, potentially using an asynchronous callback.  If the
        /// asynchronous callback is used, we will wait for it to complete.
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns></returns>
        protected AssetBase GetAsset(UUID uuid)
        {
            m_waitingForObjectAsset = true;
            m_scene.CommsManager.AssetCache.GetAsset(uuid, AssetRequestCallback, true);

            // The asset cache callback can either
            //
            // 1. Complete on the same thread (if the asset is already in the cache) or
            // 2. Come in via a different thread (if we need to go fetch it).
            //
            // The code below handles both these alternatives.
            lock (this)
            {
                if (m_waitingForObjectAsset)
                {
                    Monitor.Wait(this);
                    m_waitingForObjectAsset = false;
                }
            }

            return m_requestedObjectAsset;
        }

        /// <summary>
        /// Record the asset uuids embedded within the given script.
        /// </summary>
        /// <param name="scriptUuid"></param>
        /// <param name="assetUuids">Dictionary in which to record the references</param>
        protected void GetScriptAssetUuids(UUID scriptUuid, IDictionary<UUID, int> assetUuids)
        {
            AssetBase scriptAsset = GetAsset(scriptUuid);

            if (null != scriptAsset)
            {
                string script = Utils.BytesToString(scriptAsset.Data);
                //m_log.DebugFormat("[ARCHIVER]: Script {0}", script);
                MatchCollection uuidMatches = Util.UUIDPattern.Matches(script);
                //m_log.DebugFormat("[ARCHIVER]: Found {0} matches in script", uuidMatches.Count);

                foreach (Match uuidMatch in uuidMatches)
                {
                    UUID uuid = new UUID(uuidMatch.Value);
                    //m_log.DebugFormat("[ARCHIVER]: Recording {0} in script", uuid);
                    assetUuids[uuid] = 1;
                }
            }
        }

        /// <summary>
        /// Record the uuids referenced by the given wearable asset
        /// </summary>
        /// <param name="wearableAssetUuid"></param>
        /// <param name="assetUuids">Dictionary in which to record the references</param>
        protected void GetWearableAssetUuids(UUID wearableAssetUuid, IDictionary<UUID, int> assetUuids)
        {
            AssetBase assetBase = GetAsset(wearableAssetUuid);
            //m_log.Debug(new System.Text.ASCIIEncoding().GetString(bodypartAsset.Data));
            AssetWearable wearableAsset = new AssetBodypart(wearableAssetUuid, assetBase.Data);
            wearableAsset.Decode();

            //m_log.DebugFormat(
            //    "[ARCHIVER]: Wearable asset {0} references {1} assets", wearableAssetUuid, wearableAsset.Textures.Count);

            foreach (UUID uuid in wearableAsset.Textures.Values)
            {
                //m_log.DebugFormat("[ARCHIVER]: Got bodypart uuid {0}", uuid);
                assetUuids[uuid] = 1;
            }
        }

        /// <summary>
        /// Get all the asset uuids associated with a given object.  This includes both those directly associated with
        /// it (e.g. face textures) and recursively, those of items within it's inventory (e.g. objects contained
        /// within this object).
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="assetUuids"></param>
        protected void GetSceneObjectAssetUuids(UUID sceneObjectUuid, IDictionary<UUID, int> assetUuids)
        {
            AssetBase objectAsset = GetAsset(sceneObjectUuid);

            if (null != objectAsset)
            {
                string xml = Utils.BytesToString(objectAsset.Data);
                SceneObjectGroup sog = new SceneObjectGroup(xml, true);
                GetSceneObjectAssetUuids(sog, assetUuids);
            }
        }

        /// <summary>
        /// Get all the asset uuids associated with a given object.  This includes both those directly associated with
        /// it (e.g. face textures) and recursively, those of items within it's inventory (e.g. objects contained
        /// within this object).
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="assetUuids"></param>
        protected void GetSceneObjectAssetUuids(SceneObjectGroup sceneObject, IDictionary<UUID, int> assetUuids)
        {
            m_log.DebugFormat(
                "[ARCHIVER]: Getting assets for object {0}, {1}", sceneObject.Name, sceneObject.UUID);

            foreach (SceneObjectPart part in sceneObject.GetParts())
            {
                //m_log.DebugFormat(
                //    "[ARCHIVER]: Getting part {0}, {1} for object {2}", part.Name, part.UUID, sceneObject.UUID);

                try
                {
                    Primitive.TextureEntry textureEntry = part.Shape.Textures;

                    // Get the prim's default texture.  This will be used for faces which don't have their own texture
                    assetUuids[textureEntry.DefaultTexture.TextureID] = 1;
                    
                    // XXX: Not a great way to iterate through face textures, but there's no
                    // other method available to tell how many faces there actually are
                    //int i = 0;
                    foreach (Primitive.TextureEntryFace texture in textureEntry.FaceTextures)
                    {
                        if (texture != null)
                        {
                            //m_log.DebugFormat("[ARCHIVER]: Got face {0}", i++);
                            assetUuids[texture.TextureID] = 1;
                        }
                    }
                    
                    // If the prim is a sculpt then preserve this information too
                    if (part.Shape.SculptTexture != UUID.Zero)
                        assetUuids[part.Shape.SculptTexture] = 1;                    

                    // Now analyze this prim's inventory items to preserve all the uuids that they reference
                    foreach (TaskInventoryItem tii in part.TaskInventory.Values)
                    {
                        //m_log.DebugFormat("[ARCHIVER]: Analysing item asset type {0}", tii.Type);

                        if (!assetUuids.ContainsKey(tii.AssetID))
                        {
                            assetUuids[tii.AssetID] = 1;

                            if ((int)AssetType.Bodypart == tii.Type || ((int)AssetType.Clothing == tii.Type))
                            {
                                GetWearableAssetUuids(tii.AssetID, assetUuids);
                            }
                            else if ((int)AssetType.LSLText == tii.Type)
                            {
                                GetScriptAssetUuids(tii.AssetID, assetUuids);
                            }
                            else if ((int)AssetType.Object == tii.Type)
                            {
                                GetSceneObjectAssetUuids(tii.AssetID, assetUuids);
                            }
                            //else
                            //{
                                //m_log.DebugFormat("[ARCHIVER]: Recording asset {0} in object {1}", tii.AssetID, part.UUID);
                            //}
                        }
                    }
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[ARCHIVER]: Failed to get part - {0}", e);
                    m_log.DebugFormat("[ARCHIVER]: Texture entry length for prim was {0} (min is 46)", part.Shape.TextureEntry.Length);
                }
            }
        }

        /// <summary>
        /// Archive the region requested.
        /// </summary>
        /// <exception cref="System.IO.IOException">if there was an io problem with creating the file</exception>
        public void ArchiveRegion()
        {                            
            Dictionary<UUID, int> assetUuids = new Dictionary<UUID, int>();

            List<EntityBase> entities = m_scene.GetEntities();
            List<SceneObjectGroup> sceneObjects = new List<SceneObjectGroup>();

            // Filter entities so that we only have scene objects.
            // FIXME: Would be nicer to have this as a proper list in SceneGraph, since lots of methods
            // end up having to do this
            foreach (EntityBase entity in entities)
            {
                if (entity is SceneObjectGroup)
                {
                    SceneObjectGroup sceneObject = (SceneObjectGroup)entity;
                    
                    if (!sceneObject.IsDeleted && !sceneObject.IsAttachment)
                        sceneObjects.Add((SceneObjectGroup)entity);
                }
            }

            foreach (SceneObjectGroup sceneObject in sceneObjects)
            {
                GetSceneObjectAssetUuids(sceneObject, assetUuids);
            }

            m_log.DebugFormat(
                "[ARCHIVER]: {0} scene objects to serialize requiring save of {1} assets",
                sceneObjects.Count, assetUuids.Count);
            
            // Make sure that we also request terrain texture assets
            RegionSettings regionSettings = m_scene.RegionInfo.RegionSettings;
            
            if (regionSettings.TerrainTexture1 != RegionSettings.DEFAULT_TERRAIN_TEXTURE_1)
                assetUuids[regionSettings.TerrainTexture1] = 1;
            
            if (regionSettings.TerrainTexture2 != RegionSettings.DEFAULT_TERRAIN_TEXTURE_2)
                assetUuids[regionSettings.TerrainTexture2] = 1;
            
            if (regionSettings.TerrainTexture3 != RegionSettings.DEFAULT_TERRAIN_TEXTURE_3)
                assetUuids[regionSettings.TerrainTexture3] = 1;
            
            if (regionSettings.TerrainTexture4 != RegionSettings.DEFAULT_TERRAIN_TEXTURE_4)
                assetUuids[regionSettings.TerrainTexture4] = 1;

            // Asynchronously request all the assets required to perform this archive operation
            ArchiveWriteRequestExecution awre
                = new ArchiveWriteRequestExecution(
                    sceneObjects,
                    m_scene.RequestModuleInterface<ITerrainModule>(),
                    m_scene.RequestModuleInterface<IRegionSerialiserModule>(),
                    m_scene,
                    m_saveStream);
            
            new AssetsRequest(assetUuids.Keys, m_scene.CommsManager.AssetCache, awre.ReceivedAllAssets).Execute();
        }
    }
}