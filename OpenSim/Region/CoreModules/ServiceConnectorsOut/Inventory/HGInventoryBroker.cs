﻿/*
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
 *     * Neither the name of the OpenSimulator Project nor the
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

using log4net;
using Nini.Config;
using System;
using System.Collections.Generic;
using System.Reflection;
using OpenSim.Framework;

using OpenSim.Server.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors;
using OpenMetaverse;

namespace OpenSim.Region.CoreModules.ServiceConnectorsOut.Inventory
{
    public class HGInventoryBroker : BaseInventoryConnector, INonSharedRegionModule, IInventoryService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private static bool m_Initialized = false;
        private static bool m_Enabled = false;

        private static IInventoryService m_GridService;
        private static ISessionAuthInventoryService m_HGService;

        private Scene m_Scene;
        private IUserAccountService m_UserAccountService; 

        public Type ReplaceableInterface 
        {
            get { return null; }
        }

        public string Name
        {
            get { return "HGInventoryBroker"; }
        }

        public void Initialise(IConfigSource source)
        {
            if (!m_Initialized)
            {
                IConfig moduleConfig = source.Configs["Modules"];
                if (moduleConfig != null)
                {
                    string name = moduleConfig.GetString("InventoryServices", "");
                    if (name == Name)
                    {
                        IConfig inventoryConfig = source.Configs["InventoryService"];
                        if (inventoryConfig == null)
                        {
                            m_log.Error("[HG INVENTORY CONNECTOR]: InventoryService missing from OpenSim.ini");
                            return;
                        }

                        string localDll = inventoryConfig.GetString("LocalGridInventoryService",
                                String.Empty);
                        string HGDll = inventoryConfig.GetString("HypergridInventoryService",
                                String.Empty);

                        if (localDll == String.Empty)
                        {
                            m_log.Error("[HG INVENTORY CONNECTOR]: No LocalGridInventoryService named in section InventoryService");
                            //return;
                            throw new Exception("Unable to proceed. Please make sure your ini files in config-include are updated according to .example's");
                        }

                        if (HGDll == String.Empty)
                        {
                            m_log.Error("[HG INVENTORY CONNECTOR]: No HypergridInventoryService named in section InventoryService");
                            //return;
                            throw new Exception("Unable to proceed. Please make sure your ini files in config-include are updated according to .example's");
                        }

                        Object[] args = new Object[] { source };
                        m_GridService =
                                ServerUtils.LoadPlugin<IInventoryService>(localDll,
                                args);

                        m_HGService =
                                ServerUtils.LoadPlugin<ISessionAuthInventoryService>(HGDll,
                                args);

                        if (m_GridService == null)
                        {
                            m_log.Error("[HG INVENTORY CONNECTOR]: Can't load local inventory service");
                            return;
                        }
                        if (m_HGService == null)
                        {
                            m_log.Error("[HG INVENTORY CONNECTOR]: Can't load hypergrid inventory service");
                            return;
                        }

                        Init(source);

                        m_Enabled = true;
                        m_log.Info("[HG INVENTORY CONNECTOR]: HG inventory broker enabled");
                    }
                }
                m_Initialized = true;
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;
            m_UserAccountService = m_Scene.UserAccountService;

            scene.RegisterModuleInterface<IInventoryService>(this);
            m_cache.AddRegion(scene);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            
            m_cache.RemoveRegion(scene);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_log.InfoFormat("[HG INVENTORY CONNECTOR]: Enabled HG inventory for region {0}", scene.RegionInfo.RegionName);

        }

        #region IInventoryService

        public override bool CreateUserInventory(UUID userID)
        {
            return m_GridService.CreateUserInventory(userID);
        }

        public override List<InventoryFolderBase> GetInventorySkeleton(UUID userId)
        {
            return m_GridService.GetInventorySkeleton(userId);
        }

        public override InventoryCollection GetUserInventory(UUID userID)
        {
            return null;
        }

        public override void GetUserInventory(UUID userID, InventoryReceiptCallback callback)
        {
        }

        // Inherited. See base
        //public override InventoryFolderBase GetFolderForType(UUID userID, AssetType type)
        //{
        //    if (IsLocalGridUser(userID))
        //        return m_GridService.GetFolderForType(userID, type);
        //    else
        //    {
        //        UUID sessionID = GetSessionID(userID);
        //        string uri = GetUserInventoryURI(userID) + "/" + userID.ToString();
        //        // !!!!!!
        //        return null;
        //        //return m_HGService.GetFolderForType(uri, sessionID, type);
        //    }
        //}

        public override InventoryCollection GetFolderContent(UUID userID, UUID folderID)
        {
            string uri = string.Empty;
            if (!IsForeignUser(userID, out uri))
                return m_GridService.GetFolderContent(userID, folderID);
            else
            {
                UUID sessionID = GetSessionID(userID);
                uri = uri + "/" + userID.ToString();
                return m_HGService.GetFolderContent(uri, folderID, sessionID);
            }
        }

        public override Dictionary<AssetType, InventoryFolderBase> GetSystemFolders(UUID userID)
        {
            string uri = string.Empty;
            if (!IsForeignUser(userID, out uri))
            {
                // This is not pretty, but it will have to do for now
                if (m_GridService is BaseInventoryConnector)
                {
                    m_log.DebugFormat("[HG INVENTORY CONNECTOR]: GetSystemsFolders redirected to RemoteInventoryServiceConnector module");
                    return ((BaseInventoryConnector)m_GridService).GetSystemFolders(userID);
                }
                else
                {
                    m_log.DebugFormat("[HG INVENTORY CONNECTOR]: GetSystemsFolders redirected to GetSystemFoldersLocal");
                    return GetSystemFoldersLocal(userID);
                }
            }
            else
            {
                UUID sessionID = GetSessionID(userID);
                uri = uri + "/" + userID.ToString();
                return m_HGService.GetSystemFolders(uri, sessionID);
            }
        }

        private Dictionary<AssetType, InventoryFolderBase> GetSystemFoldersLocal(UUID userID)
        {
            InventoryFolderBase root = m_GridService.GetRootFolder(userID);
            if (root != null)
            {
                InventoryCollection content = m_GridService.GetFolderContent(userID, root.ID);
                if (content != null)
                {
                    Dictionary<AssetType, InventoryFolderBase> folders = new Dictionary<AssetType, InventoryFolderBase>();
                    foreach (InventoryFolderBase folder in content.Folders)
                    {
                        //m_log.DebugFormat("[HG INVENTORY CONNECTOR]: scanning folder type {0}", (AssetType)folder.Type);
                        if ((folder.Type != (short)AssetType.Folder) && (folder.Type != (short)AssetType.Unknown))
                            folders[(AssetType)folder.Type] = folder;
                    }
                    // Put the root folder there, as type Folder
                    folders[AssetType.Folder] = root;
                    m_log.DebugFormat("[HG INVENTORY CONNECTOR]: System folders count for {0}: {1}", userID, folders.Count);
                    return folders;
                }
                m_log.DebugFormat("[HG INVENTORY CONNECTOR]: Root folder content not found for {0}", userID);

            }

            m_log.DebugFormat("[HG INVENTORY CONNECTOR]: Root folder not found for {0}", userID);

            return new Dictionary<AssetType, InventoryFolderBase>();
        }

        public override List<InventoryItemBase> GetFolderItems(UUID userID, UUID folderID)
        {
            string uri = string.Empty;
            if (!IsForeignUser(userID, out uri))
                return m_GridService.GetFolderItems(userID, folderID);
            else
            {
                UUID sessionID = GetSessionID(userID);
                uri = uri + "/" + userID.ToString();
                return m_HGService.GetFolderItems(uri, folderID, sessionID);
            }
        }

        public override bool AddFolder(InventoryFolderBase folder)
        {
            if (folder == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(folder.Owner, out uri))
                return m_GridService.AddFolder(folder);
            else
            {
                UUID sessionID = GetSessionID(folder.Owner);
                uri = uri + "/" + folder.Owner.ToString();
                return m_HGService.AddFolder(uri, folder, sessionID);
            }
        }

        public override bool UpdateFolder(InventoryFolderBase folder)
        {
            if (folder == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(folder.Owner, out uri))
                return m_GridService.UpdateFolder(folder);
            else
            {
                UUID sessionID = GetSessionID(folder.Owner);
                uri = uri + "/" + folder.Owner.ToString();
                return m_HGService.UpdateFolder(uri, folder, sessionID);
            }
        }

        public override bool DeleteFolders(UUID ownerID, List<UUID> folderIDs)
        {
            if (folderIDs == null)
                return false;
            if (folderIDs.Count == 0)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(ownerID, out uri))
                return m_GridService.DeleteFolders(ownerID, folderIDs);
            else
            {
                UUID sessionID = GetSessionID(ownerID);
                uri = uri + "/" + ownerID.ToString();
                return m_HGService.DeleteFolders(uri, folderIDs, sessionID);
            }
        }

        public override bool MoveFolder(InventoryFolderBase folder)
        {
            if (folder == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(folder.Owner, out uri))
                return m_GridService.MoveFolder(folder);
            else
            {
                UUID sessionID = GetSessionID(folder.Owner);
                uri = uri + "/" + folder.Owner.ToString();
                return m_HGService.MoveFolder(uri, folder, sessionID);
            }
        }

        public override bool PurgeFolder(InventoryFolderBase folder)
        {
            if (folder == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(folder.Owner, out uri))
                return m_GridService.PurgeFolder(folder);
            else
            {
                UUID sessionID = GetSessionID(folder.Owner);
                uri = uri + "/" + folder.Owner.ToString();
                return m_HGService.PurgeFolder(uri, folder, sessionID);
            }
        }

        // public bool AddItem(InventoryItemBase item) inherited
        // Uses AddItemPlain

        protected override bool AddItemPlain(InventoryItemBase item)
        {
            if (item == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(item.Owner, out uri))
            {
                return m_GridService.AddItem(item);
            }
            else
            {
                UUID sessionID = GetSessionID(item.Owner);
                uri = uri + "/" + item.Owner.ToString();
                return m_HGService.AddItem(uri, item, sessionID);
            }
        }

        public override bool UpdateItem(InventoryItemBase item)
        {
            if (item == null)
                return false;

            string uri = string.Empty;
            if (!IsForeignUser(item.Owner, out uri))
                return m_GridService.UpdateItem(item);
            else
            {
                UUID sessionID = GetSessionID(item.Owner);
                uri = uri + "/" + item.Owner.ToString();
                return m_HGService.UpdateItem(uri, item, sessionID);
            }
        }

        public override bool MoveItems(UUID ownerID, List<InventoryItemBase> items)
        {
            if (items == null)
                return false;
            if (items.Count == 0)
                return true;

            string uri = string.Empty;
            if (!IsForeignUser(ownerID, out uri))
                return m_GridService.MoveItems(ownerID, items);
            else
            {
                UUID sessionID = GetSessionID(ownerID);
                uri = uri + "/" + ownerID.ToString();
                return m_HGService.MoveItems(uri, items, sessionID);
            }
        }

        public override bool DeleteItems(UUID ownerID, List<UUID> itemIDs)
        {
            m_log.DebugFormat("[HG INVENTORY CONNECTOR]: Delete {0} items for user {1}", itemIDs.Count, ownerID);

            if (itemIDs == null)
                return false;
            if (itemIDs.Count == 0)
                return true;

            string uri = string.Empty;
            if (!IsForeignUser(ownerID, out uri))
                return m_GridService.DeleteItems(ownerID, itemIDs);
            else
            {
                UUID sessionID = GetSessionID(ownerID);
                uri = uri + "/" + ownerID.ToString();
                return m_HGService.DeleteItems(uri, itemIDs, sessionID);
            }
        }

        public override InventoryItemBase GetItem(InventoryItemBase item)
        {
            if (item == null)
                return null;
            m_log.DebugFormat("[HG INVENTORY CONNECTOR]: GetItem {0} for user {1}", item.ID, item.Owner);
            string uri = string.Empty;
            if (!IsForeignUser(item.Owner, out uri))
                return m_GridService.GetItem(item);
            else
            {
                UUID sessionID = GetSessionID(item.Owner);
                uri = uri + "/" + item.Owner.ToString();
                return m_HGService.QueryItem(uri, item, sessionID);
            }
        }

        public override InventoryFolderBase GetFolder(InventoryFolderBase folder)
        {
            if (folder == null)
                return null;

            string uri = string.Empty;
            if (!IsForeignUser(folder.Owner, out uri))
                return m_GridService.GetFolder(folder);
            else
            {
                UUID sessionID = GetSessionID(folder.Owner);
                uri = uri + "/" + folder.Owner.ToString();
                return m_HGService.QueryFolder(uri, folder, sessionID);
            }
        }

        public override bool HasInventoryForUser(UUID userID)
        {
            return false;
        }

        public override List<InventoryItemBase> GetActiveGestures(UUID userId)
        {
            return new List<InventoryItemBase>();
        }

        public override int GetAssetPermissions(UUID userID, UUID assetID)
        {
            string uri = string.Empty;
            if (!IsForeignUser(userID, out uri))
                return m_GridService.GetAssetPermissions(userID, assetID);
            else
            {
                UUID sessionID = GetSessionID(userID);
                uri = uri + "/" + userID.ToString();
                return m_HGService.GetAssetPermissions(uri, assetID, sessionID);
            }
        }

        #endregion

        private UUID GetSessionID(UUID userID)
        {
            ScenePresence sp = null;
            if (m_Scene.TryGetScenePresence(userID, out sp))
            {
                return sp.ControllingClient.SessionId;
            }

            m_log.DebugFormat("[HG INVENTORY CONNECTOR]: scene presence for {0} not found", userID);
            return UUID.Zero;
        }

        private bool IsForeignUser(UUID userID, out string inventoryURL)
        {
            inventoryURL = string.Empty;
            UserAccount account = null;
            if (m_Scene.UserAccountService != null)
                account = m_Scene.UserAccountService.GetUserAccount(m_Scene.RegionInfo.ScopeID, userID);

            if (account == null) // foreign user
            {
                ScenePresence sp = null;
                m_Scene.TryGetScenePresence(userID, out sp);
                if (sp != null)
                {
                    AgentCircuitData aCircuit = m_Scene.AuthenticateHandler.GetAgentCircuitData(sp.ControllingClient.CircuitCode);
                    if (aCircuit.ServiceURLs.ContainsKey("InventoryServerURI"))
                    {
                        inventoryURL = aCircuit.ServiceURLs["InventoryServerURI"].ToString();
                        inventoryURL = inventoryURL.Trim(new char[] { '/' });
                        return true;
                    }
                }
            }
            return false;
        }

    }
}
