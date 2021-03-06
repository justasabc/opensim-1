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
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Framework
{
    /// <summary>
    /// Circuit data for an agent.  Connection information shared between
    /// regions that accept UDP connections from a client
    /// </summary>
    public class AgentCircuitData
    {
        /// <summary>
        /// Avatar Unique Agent Identifier
        /// </summary>
        public UUID AgentID;

        /// <summary>
        /// Avatar's Appearance
        /// </summary>
        public AvatarAppearance Appearance;

        /// <summary>
        /// Agent's root inventory folder
        /// </summary>
        public UUID BaseFolder;

        /// <summary>
        /// Base Caps path for user
        /// </summary>
        public string CapsPath = String.Empty;

        /// <summary>
        /// Seed caps for neighbor regions that the user can see into
        /// </summary>
        public Dictionary<ulong, string> ChildrenCapSeeds;

        /// <summary>
        /// Root agent, or Child agent
        /// </summary>
        public bool child;

        /// <summary>
        /// Number given to the client when they log-in that they provide 
        /// as credentials to the UDP server
        /// </summary>
        public uint circuitcode;

        /// <summary>
        /// How this agent got here
        /// </summary>
        public uint teleportFlags;

        /// <summary>
        /// Agent's account first name
        /// </summary>
        public string firstname;
        public UUID InventoryFolder;

        /// <summary>
        /// Agent's account last name
        /// </summary>
        public string lastname;

        /// <summary>
        /// Random Unique GUID for this session.  Client gets this at login and it's
        /// only supposed to be disclosed over secure channels
        /// </summary>
        public UUID SecureSessionID;

        /// <summary>
        /// Non secure Session ID
        /// </summary>
        public UUID SessionID;

        /// <summary>
        /// Hypergrid service token; generated by the user domain, consumed by the receiving grid.
        /// There is one such unique token for each grid visited.
        /// </summary>
        public string ServiceSessionID = string.Empty;

        /// <summary>
        /// Viewer's version string
        /// </summary>
        public string Viewer;

        /// <summary>
        /// Position the Agent's Avatar starts in the region
        /// </summary>
        public Vector3 startpos;

        public Dictionary<string, object> ServiceURLs;

        public AgentCircuitData()
        {
        }

        /// <summary>
        /// Create AgentCircuitData from a Serializable AgentCircuitData
        /// </summary>
        /// <param name="cAgent"></param>
        public AgentCircuitData(sAgentCircuitData cAgent)
        {
            AgentID = new UUID(cAgent.AgentID);
            SessionID = new UUID(cAgent.SessionID);
            SecureSessionID = new UUID(cAgent.SecureSessionID);
            startpos = new Vector3(cAgent.startposx, cAgent.startposy, cAgent.startposz);
            firstname = cAgent.firstname;
            lastname = cAgent.lastname;
            circuitcode = cAgent.circuitcode;
            child = cAgent.child;
            InventoryFolder = new UUID(cAgent.InventoryFolder);
            BaseFolder = new UUID(cAgent.BaseFolder);
            CapsPath = cAgent.CapsPath;
            ChildrenCapSeeds = cAgent.ChildrenCapSeeds;
            Viewer = cAgent.Viewer;
        }

        /// <summary>
        /// Pack AgentCircuitData into an OSDMap for transmission over LLSD XML or LLSD json
        /// </summary>
        /// <returns>map of the agent circuit data</returns>
        public OSDMap PackAgentCircuitData()
        {
            OSDMap args = new OSDMap();
            args["agent_id"] = OSD.FromUUID(AgentID);
            args["base_folder"] = OSD.FromUUID(BaseFolder);
            args["caps_path"] = OSD.FromString(CapsPath);

            if (ChildrenCapSeeds != null)
            {
                OSDArray childrenSeeds = new OSDArray(ChildrenCapSeeds.Count);
                foreach (KeyValuePair<ulong, string> kvp in ChildrenCapSeeds)
                {
                    OSDMap pair = new OSDMap();
                    pair["handle"] = OSD.FromString(kvp.Key.ToString());
                    pair["seed"] = OSD.FromString(kvp.Value);
                    childrenSeeds.Add(pair);
                }
                if (ChildrenCapSeeds.Count > 0)
                    args["children_seeds"] = childrenSeeds;
            }
            args["child"] = OSD.FromBoolean(child);
            args["circuit_code"] = OSD.FromString(circuitcode.ToString());
            args["first_name"] = OSD.FromString(firstname);
            args["last_name"] = OSD.FromString(lastname);
            args["inventory_folder"] = OSD.FromUUID(InventoryFolder);
            args["secure_session_id"] = OSD.FromUUID(SecureSessionID);
            args["session_id"] = OSD.FromUUID(SessionID);
            
            args["service_session_id"] = OSD.FromString(ServiceSessionID);
            args["start_pos"] = OSD.FromString(startpos.ToString());
            args["appearance_serial"] = OSD.FromInteger(Appearance.Serial);
            args["viewer"] = OSD.FromString(Viewer);

            if (Appearance != null)
            {
                //System.Console.WriteLine("XXX Before packing Wearables");
                if ((Appearance.Wearables != null) && (Appearance.Wearables.Length > 0))
                {
                    OSDArray wears = new OSDArray(Appearance.Wearables.Length * 2);
                    foreach (AvatarWearable awear in Appearance.Wearables)
                    {
                        wears.Add(OSD.FromUUID(awear.ItemID));
                        wears.Add(OSD.FromUUID(awear.AssetID));
                        //System.Console.WriteLine("XXX ItemID=" + awear.ItemID + " assetID=" + awear.AssetID);
                    }
                    args["wearables"] = wears;
                }

                //System.Console.WriteLine("XXX Before packing Attachments");
                Dictionary<int, UUID[]> attachments = Appearance.GetAttachmentDictionary();
                if ((attachments != null) && (attachments.Count > 0))
                {
                    OSDArray attachs = new OSDArray(attachments.Count);
                    foreach (KeyValuePair<int, UUID[]> kvp in attachments)
                    {
                        AttachmentData adata = new AttachmentData(kvp.Key, kvp.Value[0], kvp.Value[1]);
                        attachs.Add(adata.PackUpdateMessage());
                        //System.Console.WriteLine("XXX att.pt=" + kvp.Key + "; itemID=" + kvp.Value[0] + "; assetID=" + kvp.Value[1]);
                    }
                    args["attachments"] = attachs;
                }
            }

            if (ServiceURLs != null && ServiceURLs.Count > 0)
            {
                OSDArray urls = new OSDArray(ServiceURLs.Count * 2);
                foreach (KeyValuePair<string, object> kvp in ServiceURLs)
                {
                    //System.Console.WriteLine("XXX " + kvp.Key + "=" + kvp.Value);
                    urls.Add(OSD.FromString(kvp.Key));
                    urls.Add(OSD.FromString((kvp.Value == null) ? string.Empty : kvp.Value.ToString()));
                }
                args["service_urls"] = urls;
            }

            return args;
        }

        /// <summary>
        /// Unpack agent circuit data map into an AgentCiruitData object
        /// </summary>
        /// <param name="args"></param>
        public void UnpackAgentCircuitData(OSDMap args)
        {
            if (args["agent_id"] != null)
                AgentID = args["agent_id"].AsUUID();
            if (args["base_folder"] != null)
                BaseFolder = args["base_folder"].AsUUID();
            if (args["caps_path"] != null)
                CapsPath = args["caps_path"].AsString();

            if ((args["children_seeds"] != null) && (args["children_seeds"].Type == OSDType.Array))
            {
                OSDArray childrenSeeds = (OSDArray)(args["children_seeds"]);
                ChildrenCapSeeds = new Dictionary<ulong, string>();
                foreach (OSD o in childrenSeeds)
                {
                    if (o.Type == OSDType.Map)
                    {
                        ulong handle = 0;
                        string seed = "";
                        OSDMap pair = (OSDMap)o;
                        if (pair["handle"] != null)
                            if (!UInt64.TryParse(pair["handle"].AsString(), out handle))
                                continue;
                        if (pair["seed"] != null)
                            seed = pair["seed"].AsString();
                        if (!ChildrenCapSeeds.ContainsKey(handle))
                            ChildrenCapSeeds.Add(handle, seed);
                    }
                }
            }
            else
                ChildrenCapSeeds = new Dictionary<ulong, string>();

            if (args["child"] != null)
                child = args["child"].AsBoolean();
            if (args["circuit_code"] != null)
                UInt32.TryParse(args["circuit_code"].AsString(), out circuitcode);
            if (args["first_name"] != null)
                firstname = args["first_name"].AsString();
            if (args["last_name"] != null)
                lastname = args["last_name"].AsString();
            if (args["inventory_folder"] != null)
                InventoryFolder = args["inventory_folder"].AsUUID();
            if (args["secure_session_id"] != null)
                SecureSessionID = args["secure_session_id"].AsUUID();
            if (args["session_id"] != null)
                SessionID = args["session_id"].AsUUID();
            if (args["service_session_id"] != null)
                ServiceSessionID = args["service_session_id"].AsString();
            if (args["viewer"] != null)
                Viewer = args["viewer"].AsString();

            if (args["start_pos"] != null)
                Vector3.TryParse(args["start_pos"].AsString(), out startpos);

            Appearance = new AvatarAppearance(AgentID);
            if (args["appearance_serial"] != null)
                Appearance.Serial = args["appearance_serial"].AsInteger();
            if ((args["wearables"] != null) && (args["wearables"]).Type == OSDType.Array)
            {
                OSDArray wears = (OSDArray)(args["wearables"]);
                for (int i = 0; i < wears.Count / 2; i++) 
                {
                    Appearance.Wearables[i].ItemID = wears[i*2].AsUUID();
                    Appearance.Wearables[i].AssetID = wears[(i*2)+1].AsUUID();
                }
           }

            if ((args["attachments"] != null) && (args["attachments"]).Type == OSDType.Array)
            {
                OSDArray attachs = (OSDArray)(args["attachments"]);
                AttachmentData[] attachments = new AttachmentData[attachs.Count];
                int i = 0;
                foreach (OSD o in attachs)
                {
                    if (o.Type == OSDType.Map)
                    {
                        attachments[i++] = new AttachmentData((OSDMap)o);
                    }
                }
                Appearance.SetAttachments(attachments);
            }

            ServiceURLs = new Dictionary<string, object>();
            if (args.ContainsKey("service_urls") && args["service_urls"] != null && (args["service_urls"]).Type == OSDType.Array)
            {
                OSDArray urls = (OSDArray)(args["service_urls"]);
                for (int i = 0; i < urls.Count / 2; i++)
                {
                    ServiceURLs[urls[i * 2].AsString()] = urls[(i * 2) + 1].AsString();
                    //System.Console.WriteLine("XXX " + urls[i * 2].AsString() + "=" + urls[(i * 2) + 1].AsString());

                }
            }
        }
    }


    /// <summary>
    /// Serializable Agent Circuit Data
    /// </summary>
    [Serializable]
    public class sAgentCircuitData
    {
        public Guid AgentID;
        public Guid BaseFolder;
        public string CapsPath = String.Empty;
        public Dictionary<ulong, string> ChildrenCapSeeds;
        public bool child;
        public uint circuitcode;
        public string firstname;
        public Guid InventoryFolder;
        public string lastname;
        public Guid SecureSessionID;
        public Guid SessionID;
        public float startposx;
        public float startposy;
        public float startposz;
        public string Viewer;

        public sAgentCircuitData()
        {
        }

        public sAgentCircuitData(AgentCircuitData cAgent)
        {
            AgentID = cAgent.AgentID.Guid;
            SessionID = cAgent.SessionID.Guid;
            SecureSessionID = cAgent.SecureSessionID.Guid;
            startposx = cAgent.startpos.X;
            startposy = cAgent.startpos.Y;
            startposz = cAgent.startpos.Z;
            firstname = cAgent.firstname;
            lastname = cAgent.lastname;
            circuitcode = cAgent.circuitcode;
            child = cAgent.child;
            InventoryFolder = cAgent.InventoryFolder.Guid;
            BaseFolder = cAgent.BaseFolder.Guid;
            CapsPath = cAgent.CapsPath;
            ChildrenCapSeeds = cAgent.ChildrenCapSeeds;
            Viewer = cAgent.Viewer;
        }
    }
}
