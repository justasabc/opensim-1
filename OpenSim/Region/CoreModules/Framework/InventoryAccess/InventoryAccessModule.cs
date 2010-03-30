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

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;

using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Services.Interfaces;

using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using log4net;
using Nini.Config;

namespace OpenSim.Region.CoreModules.Framework.InventoryAccess
{
    public class BasicInventoryAccessModule : INonSharedRegionModule, IInventoryAccessModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled = false;
        protected Scene m_Scene;

        #region INonSharedRegionModule

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual string Name
        {
            get { return "BasicInventoryAccessModule"; }
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("InventoryAccessModule", "");
                if (name == Name)
                {
                    m_Enabled = true;
                    m_log.InfoFormat("[INVENTORY ACCESS MODULE]: {0} enabled.", Name);
                }
            }
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;

            scene.RegisterModuleInterface<IInventoryAccessModule>(this);
            scene.EventManager.OnNewClient += OnNewClient;
        }

        protected virtual void OnNewClient(IClientAPI client)
        {
            
        }

        public virtual void Close()
        {
            if (!m_Enabled)
                return;
        }


        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_Scene = null;
        }

        public virtual void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

        }

        #endregion

        #region Inventory Access

        /// <summary>
        /// Capability originating call to update the asset of an item in an agent's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public virtual UUID CapsUpdateInventoryItemAsset(IClientAPI remoteClient, UUID itemID, byte[] data)
        {
            InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
            item = m_Scene.InventoryService.GetItem(item);

            if (item != null)
            {
                if ((InventoryType)item.InvType == InventoryType.Notecard)
                {
                    if (!m_Scene.Permissions.CanEditNotecard(itemID, UUID.Zero, remoteClient.AgentId))
                    {
                        remoteClient.SendAgentAlertMessage("Insufficient permissions to edit notecard", false);
                        return UUID.Zero;
                    }

                    remoteClient.SendAgentAlertMessage("Notecard saved", false);
                }
                else if ((InventoryType)item.InvType == InventoryType.LSL)
                {
                    if (!m_Scene.Permissions.CanEditScript(itemID, UUID.Zero, remoteClient.AgentId))
                    {
                        remoteClient.SendAgentAlertMessage("Insufficient permissions to edit script", false);
                        return UUID.Zero;
                    }

                    remoteClient.SendAgentAlertMessage("Script saved", false);
                }

                AssetBase asset =
                    CreateAsset(item.Name, item.Description, (sbyte)item.AssetType, data, remoteClient.AgentId.ToString());
                item.AssetID = asset.FullID;
                m_Scene.AssetService.Store(asset);

                m_Scene.InventoryService.UpdateItem(item);

                // remoteClient.SendInventoryItemCreateUpdate(item);
                return (asset.FullID);
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Could not find item {0} for caps inventory update",
                    itemID);
            }

            return UUID.Zero;
        }

        /// <summary>
        /// Delete a scene object from a scene and place in the given avatar's inventory.
        /// Returns the UUID of the newly created asset.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="folderID"></param>
        /// <param name="objectGroup"></param>
        /// <param name="remoteClient"> </param>
        public virtual UUID DeleteToInventory(DeRezAction action, UUID folderID,
                SceneObjectGroup objectGroup, IClientAPI remoteClient)
        {
            UUID assetID = UUID.Zero;

            Vector3 inventoryStoredPosition = new Vector3
                        (((objectGroup.AbsolutePosition.X > (int)Constants.RegionSize)
                              ? 250
                              : objectGroup.AbsolutePosition.X)
                         ,
                         (objectGroup.AbsolutePosition.X > (int)Constants.RegionSize)
                             ? 250
                             : objectGroup.AbsolutePosition.X,
                         objectGroup.AbsolutePosition.Z);

            Vector3 originalPosition = objectGroup.AbsolutePosition;

            objectGroup.AbsolutePosition = inventoryStoredPosition;

            string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(objectGroup);

            objectGroup.AbsolutePosition = originalPosition;

            // Get the user info of the item destination
            //
            UUID userID = UUID.Zero;

            if (action == DeRezAction.Take || action == DeRezAction.TakeCopy ||
                action == DeRezAction.SaveToExistingUserInventoryItem)
            {
                // Take or take copy require a taker
                // Saving changes requires a local user
                //
                if (remoteClient == null)
                    return UUID.Zero;

                userID = remoteClient.AgentId;
            }
            else
            {
                // All returns / deletes go to the object owner
                //

                userID = objectGroup.RootPart.OwnerID;
            }

            if (userID == UUID.Zero) // Can't proceed
            {
                return UUID.Zero;
            }

            // If we're returning someone's item, it goes back to the
            // owner's Lost And Found folder.
            // Delete is treated like return in this case
            // Deleting your own items makes them go to trash
            //

            InventoryFolderBase folder = null;
            InventoryItemBase item = null;

            if (DeRezAction.SaveToExistingUserInventoryItem == action)
            {
                item = new InventoryItemBase(objectGroup.RootPart.FromUserInventoryItemID, userID);
                item = m_Scene.InventoryService.GetItem(item);

                //item = userInfo.RootFolder.FindItem(
                //        objectGroup.RootPart.FromUserInventoryItemID);

                if (null == item)
                {
                    m_log.DebugFormat(
                        "[AGENT INVENTORY]: Object {0} {1} scheduled for save to inventory has already been deleted.",
                        objectGroup.Name, objectGroup.UUID);
                    return UUID.Zero;
                }
            }
            else
            {
                // Folder magic
                //
                if (action == DeRezAction.Delete)
                {
                    // Deleting someone else's item
                    //


                    if (remoteClient == null ||
                        objectGroup.OwnerID != remoteClient.AgentId)
                    {
                        // Folder skeleton may not be loaded and we
                        // have to wait for the inventory to find
                        // the destination folder
                        //
                        folder = m_Scene.InventoryService.GetFolderForType(userID, AssetType.LostAndFoundFolder);
                    }
                    else
                    {
                        // Assume inventory skeleton was loaded during login
                        // and all folders can be found
                        //
                        folder = m_Scene.InventoryService.GetFolderForType(userID, AssetType.TrashFolder);
                    }
                }
                else if (action == DeRezAction.Return)
                {

                    // Dump to lost + found unconditionally
                    //
                    folder = m_Scene.InventoryService.GetFolderForType(userID, AssetType.LostAndFoundFolder);
                }

                if (folderID == UUID.Zero && folder == null)
                {
                    if (action == DeRezAction.Delete)
                    {
                        // Deletes go to trash by default
                        //
                        folder = m_Scene.InventoryService.GetFolderForType(userID, AssetType.TrashFolder);
                    }
                    else
                    {
                        // Catch all. Use lost & found
                        //

                        folder = m_Scene.InventoryService.GetFolderForType(userID, AssetType.LostAndFoundFolder);
                    }
                }

                if (folder == null) // None of the above
                {
                    //folder = userInfo.RootFolder.FindFolder(folderID);
                    folder = new InventoryFolderBase(folderID);

                    if (folder == null) // Nowhere to put it
                    {
                        return UUID.Zero;
                    }
                }

                item = new InventoryItemBase();
                item.CreatorId = objectGroup.RootPart.CreatorID.ToString();
                item.ID = UUID.Random();
                item.InvType = (int)InventoryType.Object;
                item.Folder = folder.ID;
                item.Owner = userID;
            }

            AssetBase asset = CreateAsset(
                objectGroup.GetPartName(objectGroup.RootPart.LocalId),
                objectGroup.GetPartDescription(objectGroup.RootPart.LocalId),
                (sbyte)AssetType.Object,
                Utils.StringToBytes(sceneObjectXml),
                objectGroup.OwnerID.ToString());
            m_Scene.AssetService.Store(asset);
            assetID = asset.FullID;

            if (DeRezAction.SaveToExistingUserInventoryItem == action)
            {
                item.AssetID = asset.FullID;
                m_Scene.InventoryService.UpdateItem(item);
            }
            else
            {
                item.AssetID = asset.FullID;

                if (remoteClient != null && (remoteClient.AgentId != objectGroup.RootPart.OwnerID) && m_Scene.Permissions.PropagatePermissions())
                {
                    uint perms = objectGroup.GetEffectivePermissions();
                    uint nextPerms = (perms & 7) << 13;
                    if ((nextPerms & (uint)PermissionMask.Copy) == 0)
                        perms &= ~(uint)PermissionMask.Copy;
                    if ((nextPerms & (uint)PermissionMask.Transfer) == 0)
                        perms &= ~(uint)PermissionMask.Transfer;
                    if ((nextPerms & (uint)PermissionMask.Modify) == 0)
                        perms &= ~(uint)PermissionMask.Modify;

                    item.BasePermissions = perms & objectGroup.RootPart.NextOwnerMask;
                    item.CurrentPermissions = item.BasePermissions;
                    item.NextPermissions = objectGroup.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = objectGroup.RootPart.EveryoneMask & objectGroup.RootPart.NextOwnerMask;
                    item.GroupPermissions = objectGroup.RootPart.GroupMask & objectGroup.RootPart.NextOwnerMask;
                    item.CurrentPermissions |= 8; // Slam!
                }
                else
                {
                    item.BasePermissions = objectGroup.GetEffectivePermissions();
                    item.CurrentPermissions = objectGroup.GetEffectivePermissions();
                    item.NextPermissions = objectGroup.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = objectGroup.RootPart.EveryoneMask;
                    item.GroupPermissions = objectGroup.RootPart.GroupMask;

                    item.CurrentPermissions |= 8; // Slam!
                }

                // TODO: add the new fields (Flags, Sale info, etc)
                item.CreationDate = Util.UnixTimeSinceEpoch();
                item.Description = asset.Description;
                item.Name = asset.Name;
                item.AssetType = asset.Type;

                m_Scene.InventoryService.AddItem(item);

                if (remoteClient != null && item.Owner == remoteClient.AgentId)
                {
                    remoteClient.SendInventoryItemCreateUpdate(item, 0);
                }
                else
                {
                    ScenePresence notifyUser = m_Scene.GetScenePresence(item.Owner);
                    if (notifyUser != null)
                    {
                        notifyUser.ControllingClient.SendInventoryItemCreateUpdate(item, 0);
                    }
                }
            }

            return assetID;
        }


        /// <summary>
        /// Rez an object into the scene from the user's inventory
        /// </summary>
        /// FIXME: It would be really nice if inventory access modules didn't also actually do the work of rezzing
        /// things to the scene.  The caller should be doing that, I think.
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="RayEnd"></param>
        /// <param name="RayStart"></param>
        /// <param name="RayTargetID"></param>
        /// <param name="BypassRayCast"></param>
        /// <param name="RayEndIsIntersection"></param>
        /// <param name="RezSelected"></param>
        /// <param name="RemoveItem"></param>
        /// <param name="fromTaskID"></param>
        /// <param name="attachment"></param>
        /// <returns>The SceneObjectGroup rezzed or null if rez was unsuccessful.</returns>
        public virtual SceneObjectGroup RezObject(IClientAPI remoteClient, UUID itemID, Vector3 RayEnd, Vector3 RayStart,
                                    UUID RayTargetID, byte BypassRayCast, bool RayEndIsIntersection,
                                    bool RezSelected, bool RemoveItem, UUID fromTaskID, bool attachment)
        {
            // Work out position details
            byte bRayEndIsIntersection = (byte)0;

            if (RayEndIsIntersection)
            {
                bRayEndIsIntersection = (byte)1;
            }
            else
            {
                bRayEndIsIntersection = (byte)0;
            }

            Vector3 scale = new Vector3(0.5f, 0.5f, 0.5f);


            Vector3 pos = m_Scene.GetNewRezLocation(
                      RayStart, RayEnd, RayTargetID, Quaternion.Identity,
                      BypassRayCast, bRayEndIsIntersection, true, scale, false);

            // Rez object
            InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
            item = m_Scene.InventoryService.GetItem(item);

            if (item != null)
            {
                AssetBase rezAsset = m_Scene.AssetService.Get(item.AssetID.ToString());

                if (rezAsset != null)
                {
                    UUID itemId = UUID.Zero;

                    // If we have permission to copy then link the rezzed object back to the user inventory
                    // item that it came from.  This allows us to enable 'save object to inventory'
                    if (!m_Scene.Permissions.BypassPermissions())
                    {
                        if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == (uint)PermissionMask.Copy)
                        {
                            itemId = item.ID;
                        }
                    }
                    else
                    {
                        // Brave new fullperm world
                        //
                        itemId = item.ID;
                    }

                    string xmlData = Utils.BytesToString(rezAsset.Data);
                    SceneObjectGroup group
                        = SceneObjectSerializer.FromOriginalXmlFormat(itemId, xmlData);

                    if (!m_Scene.Permissions.CanRezObject(
                        group.Children.Count, remoteClient.AgentId, pos)
                        && !attachment)
                    {
                        // The client operates in no fail mode. It will
                        // have already removed the item from the folder
                        // if it's no copy.
                        // Put it back if it's not an attachment
                        //
                        if (((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0) && (!attachment))
                            remoteClient.SendBulkUpdateInventory(item);
                        return null;
                    }

                    group.ResetIDs();

                    if (attachment)
                    {
                        group.RootPart.ObjectFlags |= (uint)PrimFlags.Phantom;
                        group.RootPart.IsAttachment = true;
                    }

                    // For attachments, we must make sure that only a single object update occurs after we've finished
                    // all the necessary operations.
                    m_Scene.AddNewSceneObject(group, true, false);

                    //  m_log.InfoFormat("ray end point for inventory rezz is {0} {1} {2} ", RayEnd.X, RayEnd.Y, RayEnd.Z);
                    // if attachment we set it's asset id so object updates can reflect that
                    // if not, we set it's position in world.
                    if (!attachment)
                    {
                        group.ScheduleGroupForFullUpdate();
                        
                        float offsetHeight = 0;
                        pos = m_Scene.GetNewRezLocation(
                            RayStart, RayEnd, RayTargetID, Quaternion.Identity,
                            BypassRayCast, bRayEndIsIntersection, true, group.GetAxisAlignedBoundingBox(out offsetHeight), false);
                        pos.Z += offsetHeight;
                        group.AbsolutePosition = pos;
                        //   m_log.InfoFormat("rezx point for inventory rezz is {0} {1} {2}  and offsetheight was {3}", pos.X, pos.Y, pos.Z, offsetHeight);

                    }
                    else
                    {
                        group.SetFromItemID(itemID);
                    }

                    SceneObjectPart rootPart = null;
                    try
                    {
                        rootPart = group.GetChildPart(group.UUID);
                    }
                    catch (NullReferenceException)
                    {
                        string isAttachment = "";

                        if (attachment)
                            isAttachment = " Object was an attachment";

                        m_log.Error("[AGENT INVENTORY]: Error rezzing ItemID: " + itemID + " object has no rootpart." + isAttachment);
                    }

                    // Since renaming the item in the inventory does not affect the name stored
                    // in the serialization, transfer the correct name from the inventory to the
                    // object itself before we rez.
                    rootPart.Name = item.Name;
                    rootPart.Description = item.Description;

                    List<SceneObjectPart> partList = new List<SceneObjectPart>(group.Children.Values);

                    group.SetGroup(remoteClient.ActiveGroupId, remoteClient);
                    if (rootPart.OwnerID != item.Owner)
                    {
                        //Need to kill the for sale here
                        rootPart.ObjectSaleType = 0;
                        rootPart.SalePrice = 10;

                        if (m_Scene.Permissions.PropagatePermissions())
                        {
                            if ((item.CurrentPermissions & 8) != 0)
                            {
                                foreach (SceneObjectPart part in partList)
                                {
                                    part.EveryoneMask = item.EveryOnePermissions;
                                    part.NextOwnerMask = item.NextPermissions;
                                    part.GroupMask = 0; // DO NOT propagate here
                                }
                            }
                            
                            group.ApplyNextOwnerPermissions();
                        }
                    }

                    foreach (SceneObjectPart part in partList)
                    {
                        if (part.OwnerID != item.Owner)
                        {
                            part.LastOwnerID = part.OwnerID;
                            part.OwnerID = item.Owner;
                            part.Inventory.ChangeInventoryOwner(item.Owner);
                        }
                        else if (((item.CurrentPermissions & 8) != 0) && (!attachment)) // Slam!
                        {
                            part.EveryoneMask = item.EveryOnePermissions;
                            part.NextOwnerMask = item.NextPermissions;

                            part.GroupMask = 0; // DO NOT propagate here
                        }
                    }

                    rootPart.TrimPermissions();

                    if (!attachment)
                    {
                        if (group.RootPart.Shape.PCode == (byte)PCode.Prim)
                        {
                            group.ClearPartAttachmentData();
                        }
                        
                        // Fire on_rez
                        group.CreateScriptInstances(0, true, m_Scene.DefaultScriptEngine, 0);

                        rootPart.ScheduleFullUpdate();
                    }

                    if (!m_Scene.Permissions.BypassPermissions())
                    {
                        if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                        {
                            // If this is done on attachments, no
                            // copy ones will be lost, so avoid it
                            //
                            if (!attachment)
                            {
                                List<UUID> uuids = new List<UUID>();
                                uuids.Add(item.ID);
                                m_Scene.InventoryService.DeleteItems(item.Owner, uuids);
                            }
                        }
                    }

                    return rootPart.ParentGroup;
                }
            }

            return null;
        }

        public virtual void TransferInventoryAssets(InventoryItemBase item, UUID sender, UUID receiver)
        {
        }

        #endregion

        #region Misc

        /// <summary>
        /// Create a new asset data structure.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="invType"></param>
        /// <param name="assetType"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        private AssetBase CreateAsset(string name, string description, sbyte assetType, byte[] data, string creatorID)
        {
            AssetBase asset = new AssetBase(UUID.Random(), name, assetType, creatorID);
            asset.Description = description;
            asset.Data = (data == null) ? new byte[1] : data;

            return asset;
        }

        #endregion
    }
}
