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
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Services.Connectors.SimianGrid
{
    /// <summary>
    /// Connects avatar appearance data to the SimianGrid backend
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class SimianAvatarServiceConnector : IAvatarService, ISharedRegionModule
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
        private static string ZeroID = UUID.Zero.ToString();

        private string m_serverUrl = String.Empty;

        #region ISharedRegionModule

        public Type ReplaceableInterface { get { return null; } }
        public void RegionLoaded(Scene scene) { }
        public void PostInitialise() { }
        public void Close() { }

        public SimianAvatarServiceConnector() { }
        public string Name { get { return "SimianAvatarServiceConnector"; } }
        public void AddRegion(Scene scene) { if (!String.IsNullOrEmpty(m_serverUrl)) { scene.RegisterModuleInterface<IAvatarService>(this); } }
        public void RemoveRegion(Scene scene) { if (!String.IsNullOrEmpty(m_serverUrl)) { scene.UnregisterModuleInterface<IAvatarService>(this); } }

        #endregion ISharedRegionModule

        public SimianAvatarServiceConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public void Initialise(IConfigSource source)
        {
            if (Simian.IsSimianEnabled(source, "AvatarServices", this.Name))
            {
                IConfig gridConfig = source.Configs["AvatarService"];
                if (gridConfig == null)
                {
                    m_log.Error("[SIMIAN AVATAR CONNECTOR]: AvatarService missing from OpenSim.ini");
                    throw new Exception("Avatar connector init error");
                }

                string serviceUrl = gridConfig.GetString("AvatarServerURI");
                if (String.IsNullOrEmpty(serviceUrl))
                {
                    m_log.Error("[SIMIAN AVATAR CONNECTOR]: No AvatarServerURI in section AvatarService");
                    throw new Exception("Avatar connector init error");
                }

                if (!serviceUrl.EndsWith("/"))
                    serviceUrl = serviceUrl + '/';

                m_serverUrl = serviceUrl;
            }
        }

        #region IAvatarService

        public AvatarData GetAvatar(UUID userID)
        {
            NameValueCollection requestArgs = new NameValueCollection
            {
                { "RequestMethod", "GetUser" },
                { "UserID", userID.ToString() }
            };

            OSDMap response = WebUtil.PostToService(m_serverUrl, requestArgs);
            if (response["Success"].AsBoolean())
            {
                OSDMap map = null;
                try { map = OSDParser.DeserializeJson(response["LLAppearance"].AsString()) as OSDMap; }
                catch { }

                if (map != null)
                {
                    AvatarWearable[] wearables = new AvatarWearable[13];
                    wearables[0] = new AvatarWearable(map["ShapeItem"].AsUUID(), map["ShapeAsset"].AsUUID());
                    wearables[1] = new AvatarWearable(map["SkinItem"].AsUUID(), map["SkinAsset"].AsUUID());
                    wearables[2] = new AvatarWearable(map["HairItem"].AsUUID(), map["HairAsset"].AsUUID());
                    wearables[3] = new AvatarWearable(map["EyesItem"].AsUUID(), map["EyesAsset"].AsUUID());
                    wearables[4] = new AvatarWearable(map["ShirtItem"].AsUUID(), map["ShirtAsset"].AsUUID());
                    wearables[5] = new AvatarWearable(map["PantsItem"].AsUUID(), map["PantsAsset"].AsUUID());
                    wearables[6] = new AvatarWearable(map["ShoesItem"].AsUUID(), map["ShoesAsset"].AsUUID());
                    wearables[7] = new AvatarWearable(map["SocksItem"].AsUUID(), map["SocksAsset"].AsUUID());
                    wearables[8] = new AvatarWearable(map["JacketItem"].AsUUID(), map["JacketAsset"].AsUUID());
                    wearables[9] = new AvatarWearable(map["GlovesItem"].AsUUID(), map["GlovesAsset"].AsUUID());
                    wearables[10] = new AvatarWearable(map["UndershirtItem"].AsUUID(), map["UndershirtAsset"].AsUUID());
                    wearables[11] = new AvatarWearable(map["UnderpantsItem"].AsUUID(), map["UnderpantsAsset"].AsUUID());
                    wearables[12] = new AvatarWearable(map["SkirtItem"].AsUUID(), map["SkirtAsset"].AsUUID());

                    AvatarAppearance appearance = new AvatarAppearance(userID);
                    appearance.Wearables = wearables;
                    appearance.AvatarHeight = (float)map["Height"].AsReal();

                    AvatarData avatar = new AvatarData(appearance);
                    
                    // Get attachments
                    map = null;
                    try { map = OSDParser.DeserializeJson(response["LLAttachments"].AsString()) as OSDMap; }
                    catch { }

                    if (map != null)
                    {
                        foreach (KeyValuePair<string, OSD> kvp in map)
                            avatar.Data[kvp.Key] = kvp.Value.AsString();
                    }

                    return avatar;
                }
                else
                {
                    m_log.Warn("[SIMIAN AVATAR CONNECTOR]: Failed to get user appearance for " + userID +
                        ", LLAppearance is missing or invalid");
                    return null;
                }
            }
            else
            {
                m_log.Warn("[SIMIAN AVATAR CONNECTOR]: Failed to get user appearance for " + userID + ": " +
                    response["Message"].AsString());
            }

            return null;
        }

        public bool SetAvatar(UUID userID, AvatarData avatar)
        {
            m_log.Debug("[SIMIAN AVATAR CONNECTOR]: SetAvatar called for " + userID);

            if (avatar.AvatarType == 1) // LLAvatar
            {
                AvatarAppearance appearance = avatar.ToAvatarAppearance(userID);

                OSDMap map = new OSDMap();

                map["Height"] = OSD.FromReal(appearance.AvatarHeight);

                map["ShapeItem"] = OSD.FromUUID(appearance.BodyItem);
                map["ShapeAsset"] = OSD.FromUUID(appearance.BodyAsset);
                map["SkinItem"] = OSD.FromUUID(appearance.SkinItem);
                map["SkinAsset"] = OSD.FromUUID(appearance.SkinAsset);
                map["HairItem"] = OSD.FromUUID(appearance.HairItem);
                map["HairAsset"] = OSD.FromUUID(appearance.HairAsset);
                map["EyesItem"] = OSD.FromUUID(appearance.EyesItem);
                map["EyesAsset"] = OSD.FromUUID(appearance.EyesAsset);
                map["ShirtItem"] = OSD.FromUUID(appearance.ShirtItem);
                map["ShirtAsset"] = OSD.FromUUID(appearance.ShirtAsset);
                map["PantsItem"] = OSD.FromUUID(appearance.PantsItem);
                map["PantsAsset"] = OSD.FromUUID(appearance.PantsAsset);
                map["ShoesItem"] = OSD.FromUUID(appearance.ShoesItem);
                map["ShoesAsset"] = OSD.FromUUID(appearance.ShoesAsset);
                map["SocksItem"] = OSD.FromUUID(appearance.SocksItem);
                map["SocksAsset"] = OSD.FromUUID(appearance.SocksAsset);
                map["JacketItem"] = OSD.FromUUID(appearance.JacketItem);
                map["JacketAsset"] = OSD.FromUUID(appearance.JacketAsset);
                map["GlovesItem"] = OSD.FromUUID(appearance.GlovesItem);
                map["GlovesAsset"] = OSD.FromUUID(appearance.GlovesAsset);
                map["UndershirtItem"] = OSD.FromUUID(appearance.UnderShirtItem);
                map["UndershirtAsset"] = OSD.FromUUID(appearance.UnderShirtAsset);
                map["UnderpantsItem"] = OSD.FromUUID(appearance.UnderPantsItem);
                map["UnderpantsAsset"] = OSD.FromUUID(appearance.UnderPantsAsset);
                map["SkirtItem"] = OSD.FromUUID(appearance.SkirtItem);
                map["SkirtAsset"] = OSD.FromUUID(appearance.SkirtAsset);

                OSDMap items = new OSDMap();
                foreach (KeyValuePair<string, string> kvp in avatar.Data)
                {
                    if (kvp.Key.StartsWith("_ap_"))
                        items.Add(kvp.Key, OSD.FromString(kvp.Value));
                }

                NameValueCollection requestArgs = new NameValueCollection
                {
                    { "RequestMethod", "AddUserData" },
                    { "UserID", userID.ToString() },
                    { "LLAppearance", OSDParser.SerializeJsonString(map) },
                    { "LLAttachments", OSDParser.SerializeJsonString(items) }
                };

                OSDMap response = WebUtil.PostToService(m_serverUrl, requestArgs);
                bool success = response["Success"].AsBoolean();

                if (!success)
                    m_log.Warn("[SIMIAN AVATAR CONNECTOR]: Failed saving appearance for " + userID + ": " + response["Message"].AsString());

                return success;
            }
            else
            {
                m_log.Error("[SIMIAN AVATAR CONNECTOR]: Can't save appearance for " + userID + ". Unhandled avatar type " + avatar.AvatarType);
                return false;
            }
        }

        public bool ResetAvatar(UUID userID)
        {
            m_log.Error("[SIMIAN AVATAR CONNECTOR]: ResetAvatar called for " + userID + ", implement this");
            return false;
        }

        public bool SetItems(UUID userID, string[] names, string[] values)
        {
            m_log.Error("[SIMIAN AVATAR CONNECTOR]: SetItems called for " + userID + " with " + names.Length + " names and " + values.Length + " values, implement this");
            return false;
        }

        public bool RemoveItems(UUID userID, string[] names)
        {
            m_log.Error("[SIMIAN AVATAR CONNECTOR]: RemoveItems called for " + userID + " with " + names.Length + " names, implement this");
            return false;
        }

        #endregion IAvatarService
    }
}
