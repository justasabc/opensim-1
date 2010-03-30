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
using System.Net;
using System.Reflection;
using System.Xml;

using Nini.Config;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Data;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Hypergrid;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenMetaverse;

namespace OpenSim.Services.GridService
{
    public class HypergridLinker 
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private static UUID m_HGMapImage = new UUID("00000000-0000-1111-9999-000000000013");

        private static uint m_autoMappingX = 0;
        private static uint m_autoMappingY = 0;
        private static bool m_enableAutoMapping = false;

        protected IRegionData m_Database;
        protected GridService m_GridService;
        protected IAssetService m_AssetService;
        protected GatekeeperServiceConnector m_GatekeeperConnector;

        protected UUID m_ScopeID = UUID.Zero;

        // Hyperlink regions are hyperlinks on the map
        public readonly Dictionary<UUID, GridRegion> m_HyperlinkRegions = new Dictionary<UUID, GridRegion>();
        protected Dictionary<UUID, ulong> m_HyperlinkHandles = new Dictionary<UUID, ulong>();

        protected GridRegion m_DefaultRegion;
        protected GridRegion DefaultRegion
        {
            get
            {
                if (m_DefaultRegion == null)
                {
                    List<GridRegion> defs = m_GridService.GetDefaultRegions(m_ScopeID);
                    if (defs != null && defs.Count > 0)
                        m_DefaultRegion = defs[0];
                    else
                    {
                        // Best guess, may be totally off
                        m_DefaultRegion = new GridRegion(1000, 1000);
                        m_log.WarnFormat("[HYPERGRID LINKER]: This grid does not have a default region. Assuming default coordinates at 1000, 1000.");
                    }
                }
                return m_DefaultRegion;
            }
        }

        public HypergridLinker(IConfigSource config, GridService gridService, IRegionData db)
        {
            m_log.DebugFormat("[HYPERGRID LINKER]: Starting...");

            m_Database = db;
            m_GridService = gridService;

            IConfig gridConfig = config.Configs["GridService"];
            if (gridConfig != null)
            {
                string assetService = gridConfig.GetString("AssetService", string.Empty);

                Object[] args = new Object[] { config };

                if (assetService != string.Empty)
                    m_AssetService = ServerUtils.LoadPlugin<IAssetService>(assetService, args);

                string scope = gridConfig.GetString("ScopeID", string.Empty);
                if (scope != string.Empty)
                    UUID.TryParse(scope, out m_ScopeID);

                m_GatekeeperConnector = new GatekeeperServiceConnector(m_AssetService);

                m_log.DebugFormat("[HYPERGRID LINKER]: Loaded all services...");
            }

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand("hypergrid", false, "link-region",
                    "link-region <Xloc> <Yloc> <HostName>:<HttpPort>[:<RemoteRegionName>] <cr>",
                    "Link a hypergrid region", RunCommand);
                MainConsole.Instance.Commands.AddCommand("hypergrid", false, "unlink-region",
                    "unlink-region <local name> or <HostName>:<HttpPort> <cr>",
                    "Unlink a hypergrid region", RunCommand);
                MainConsole.Instance.Commands.AddCommand("hypergrid", false, "link-mapping", "link-mapping [<x> <y>] <cr>",
                    "Set local coordinate to map HG regions to", RunCommand);
                MainConsole.Instance.Commands.AddCommand("hypergrid", false, "show hyperlinks", "show hyperlinks <cr>",
                    "List the HG regions", HandleShow);
            }
        }


        #region Link Region

        public GridRegion LinkRegion(UUID scopeID, string regionDescriptor)
        {
            string reason = string.Empty;
            int xloc = random.Next(0, Int16.MaxValue) * (int)Constants.RegionSize;
            return TryLinkRegionToCoords(scopeID, regionDescriptor, xloc, 0, out reason);
        }

        private static Random random = new Random();

        // From the command line link-region
        public GridRegion TryLinkRegionToCoords(UUID scopeID, string mapName, int xloc, int yloc, out string reason)
        {
            reason = string.Empty;
            string host = "127.0.0.1";
            string portstr;
            string regionName = "";
            uint port = 9000;
            string[] parts = mapName.Split(new char[] { ':' });
            if (parts.Length >= 1)
            {
                host = parts[0];
            }
            if (parts.Length >= 2)
            {
                portstr = parts[1];
                //m_log.Debug("-- port = " + portstr);
                if (!UInt32.TryParse(portstr, out port))
                    regionName = parts[1];
            }
            // always take the last one
            if (parts.Length >= 3)
            {
                regionName = parts[2];
            }

            // Sanity check. 
            //IPAddress ipaddr = null;
            try
            {
                //ipaddr = Util.GetHostFromDNS(host);
                Util.GetHostFromDNS(host);
            }
            catch 
            {
                reason = "Malformed hostname";
                return null;
            }

            GridRegion regInfo;
            bool success = TryCreateLink(scopeID, xloc, yloc, regionName, port, host, out regInfo, out reason);
            if (success)
            {
                regInfo.RegionName = mapName;
                return regInfo;
            }

            return null;
        }


        // From the command line and the 2 above
        public bool TryCreateLink(UUID scopeID, int xloc, int yloc,
            string externalRegionName, uint externalPort, string externalHostName, out GridRegion regInfo, out string reason)
        {
            m_log.DebugFormat("[HYPERGRID LINKER]: Link to {0}:{1}, in {2}-{3}", externalHostName, externalPort, xloc, yloc);

            reason = string.Empty;
            regInfo = new GridRegion();
            regInfo.RegionName = externalRegionName;
            regInfo.HttpPort = externalPort;
            regInfo.ExternalHostName = externalHostName;
            regInfo.RegionLocX = xloc;
            regInfo.RegionLocY = yloc;
            regInfo.ScopeID = scopeID;

            try
            {
                regInfo.InternalEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), (int)0);
            }
            catch (Exception e)
            {
                m_log.Warn("[HYPERGRID LINKER]: Wrong format for link-region: " + e.Message);
                reason = "Internal error";
                return false;
            }

            // Finally, link it
            ulong handle = 0;
            UUID regionID = UUID.Zero;
            string externalName = string.Empty;
            string imageURL = string.Empty;
            if (!m_GatekeeperConnector.LinkRegion(regInfo, out regionID, out handle, out externalName, out imageURL, out reason))
                return false;

            if (regionID != UUID.Zero)
            {
                GridRegion r = m_GridService.GetRegionByUUID(scopeID, regionID);
                if (r != null)
                {
                    m_log.DebugFormat("[HYPERGRID LINKER]: Region already exists in coordinates {0} {1}", r.RegionLocX / Constants.RegionSize, r.RegionLocY / Constants.RegionSize);
                    regInfo = r;
                    return true;
                }

                regInfo.RegionID = regionID;
                Uri uri = null;
                try
                {
                    uri = new Uri(externalName);
                    regInfo.ExternalHostName = uri.Host;
                    regInfo.HttpPort = (uint)uri.Port;
                }
                catch 
                {
                    m_log.WarnFormat("[HYPERGRID LINKER]: Remote Gatekeeper at {0} provided malformed ExternalName {1}", regInfo.ExternalHostName, externalName);
                }
                regInfo.RegionName = regInfo.ExternalHostName + ":" + regInfo.HttpPort + ":" + regInfo.RegionName;
                // Try get the map image
                //regInfo.TerrainImage = m_GatekeeperConnector.GetMapImage(regionID, imageURL);
                // I need a texture that works for this... the one I tried doesn't seem to be working
                regInfo.TerrainImage = m_HGMapImage;

                AddHyperlinkRegion(regInfo, handle);
                m_log.Info("[HYPERGRID LINKER]: Successfully linked to region_uuid " + regInfo.RegionID);

            }
            else
            {
                m_log.Warn("[HYPERGRID LINKER]: Unable to link region");
                reason = "Remote region could not be found";
                return false;
            }

            uint x, y;
            if (!Check4096(handle, out x, out y))
            {
                RemoveHyperlinkRegion(regInfo.RegionID);
                reason = "Region is too far (" + x + ", " + y + ")";
                m_log.Info("[HYPERGRID LINKER]: Unable to link, region is too far (" + x + ", " + y + ")");
                return false;
            }

            m_log.Debug("[HYPERGRID LINKER]: link region succeeded");
            return true;
        }

        public bool TryUnlinkRegion(string mapName)
        {
            GridRegion regInfo = null;
            if (mapName.Contains(":"))
            {
                string host = "127.0.0.1";
                //string portstr;
                //string regionName = "";
                uint port = 9000;
                string[] parts = mapName.Split(new char[] { ':' });
                if (parts.Length >= 1)
                {
                    host = parts[0];
                }

                foreach (GridRegion r in m_HyperlinkRegions.Values)
                    if (host.Equals(r.ExternalHostName) && (port == r.HttpPort))
                        regInfo = r;
            }
            else
            {
                foreach (GridRegion r in m_HyperlinkRegions.Values)
                    if (r.RegionName.Equals(mapName))
                        regInfo = r;
            }
            if (regInfo != null)
            {
                RemoveHyperlinkRegion(regInfo.RegionID);
                return true;
            }
            else
            {
                m_log.InfoFormat("[HYPERGRID LINKER]: Region {0} not found", mapName);
                return false;
            }
        }

        /// <summary>
        /// Cope with this viewer limitation.
        /// </summary>
        /// <param name="regInfo"></param>
        /// <returns></returns>
        public bool Check4096(ulong realHandle, out uint x, out uint y)
        {
            GridRegion defRegion = DefaultRegion;

            uint ux = 0, uy = 0;
            Utils.LongToUInts(realHandle, out ux, out uy);
            x = ux / Constants.RegionSize;
            y = uy / Constants.RegionSize;

            if ((Math.Abs((int)defRegion.RegionLocX - ux) >= 4096 * Constants.RegionSize) ||
                (Math.Abs((int)defRegion.RegionLocY - uy) >= 4096 * Constants.RegionSize))
            {
                return false;
            }
            return true;
        }

        private void AddHyperlinkRegion(GridRegion regionInfo, ulong regionHandle)
        {
            //m_HyperlinkRegions[regionInfo.RegionID] = regionInfo;
            //m_HyperlinkHandles[regionInfo.RegionID] = regionHandle;

            RegionData rdata = m_GridService.RegionInfo2RegionData(regionInfo);
            int flags = (int)OpenSim.Data.RegionFlags.Hyperlink + (int)OpenSim.Data.RegionFlags.NoDirectLogin + (int)OpenSim.Data.RegionFlags.RegionOnline;
            rdata.Data["flags"] = flags.ToString();

            m_Database.Store(rdata);

        }

        private void RemoveHyperlinkRegion(UUID regionID)
        {
            //// Try the hyperlink collection
            //if (m_HyperlinkRegions.ContainsKey(regionID))
            //{
            //    m_HyperlinkRegions.Remove(regionID);
            //    m_HyperlinkHandles.Remove(regionID);
            //}
            m_Database.Delete(regionID);
        }

        #endregion


        #region Console Commands

        public void HandleShow(string module, string[] cmd)
        {
            MainConsole.Instance.Output("Not Implemented Yet");
            //if (cmd.Length != 2)
            //{
            //    MainConsole.Instance.Output("Syntax: show hyperlinks");
            //    return;
            //}
            //List<GridRegion> regions = new List<GridRegion>(m_HypergridService.m_HyperlinkRegions.Values);
            //if (regions == null || regions.Count < 1)
            //{
            //    MainConsole.Instance.Output("No hyperlinks");
            //    return;
            //}

            //MainConsole.Instance.Output("Region Name          Region UUID");
            //MainConsole.Instance.Output("Location             URI");
            //MainConsole.Instance.Output("Owner ID  ");
            //MainConsole.Instance.Output("-------------------------------------------------------------------------------");
            //foreach (GridRegion r in regions)
            //{
            //    MainConsole.Instance.Output(String.Format("{0,-20} {1}\n{2,-20} {3}\n{4,-39} \n\n",
            //            r.RegionName, r.RegionID,
            //            String.Format("{0},{1}", r.RegionLocX, r.RegionLocY), "http://" + r.ExternalHostName + ":" + r.HttpPort.ToString(),
            //            r.EstateOwner.ToString()));
            //}
            //return;
        }
        public void RunCommand(string module, string[] cmdparams)
        {
            List<string> args = new List<string>(cmdparams);
            if (args.Count < 1)
                return;

            string command = args[0];
            args.RemoveAt(0);

            cmdparams = args.ToArray();

            RunHGCommand(command, cmdparams);

        }

        private void RunHGCommand(string command, string[] cmdparams)
        {
            if (command.Equals("link-mapping"))
            {
                if (cmdparams.Length == 2)
                {
                    try
                    {
                        m_autoMappingX = Convert.ToUInt32(cmdparams[0]);
                        m_autoMappingY = Convert.ToUInt32(cmdparams[1]);
                        m_enableAutoMapping = true;
                    }
                    catch (Exception)
                    {
                        m_autoMappingX = 0;
                        m_autoMappingY = 0;
                        m_enableAutoMapping = false;
                    }
                }
            }
            else if (command.Equals("link-region"))
            {
                if (cmdparams.Length < 3)
                {
                    if ((cmdparams.Length == 1) || (cmdparams.Length == 2))
                    {
                        LoadXmlLinkFile(cmdparams);
                    }
                    else
                    {
                        LinkRegionCmdUsage();
                    }
                    return;
                }

                if (cmdparams[2].Contains(":"))
                {
                    // New format
                    int xloc, yloc;
                    string mapName;
                    try
                    {
                        xloc = Convert.ToInt32(cmdparams[0]);
                        yloc = Convert.ToInt32(cmdparams[1]);
                        mapName = cmdparams[2];
                        if (cmdparams.Length > 3)
                            for (int i = 3; i < cmdparams.Length; i++)
                                mapName += " " + cmdparams[i];

                        //m_log.Info(">> MapName: " + mapName);
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.Output("[HGrid] Wrong format for link-region command: " + e.Message);
                        LinkRegionCmdUsage();
                        return;
                    }

                    // Convert cell coordinates given by the user to meters
                    xloc = xloc * (int)Constants.RegionSize;
                    yloc = yloc * (int)Constants.RegionSize;
                    string reason = string.Empty;
                    if (TryLinkRegionToCoords(UUID.Zero, mapName, xloc, yloc, out reason) == null)
                        MainConsole.Instance.Output("Failed to link region: " + reason);
                    else
                        MainConsole.Instance.Output("Hyperlink established");
                }
                else
                {
                    // old format
                    GridRegion regInfo;
                    int xloc, yloc;
                    uint externalPort;
                    string externalHostName;
                    try
                    {
                        xloc = Convert.ToInt32(cmdparams[0]);
                        yloc = Convert.ToInt32(cmdparams[1]);
                        externalPort = Convert.ToUInt32(cmdparams[3]);
                        externalHostName = cmdparams[2];
                        //internalPort = Convert.ToUInt32(cmdparams[4]);
                        //remotingPort = Convert.ToUInt32(cmdparams[5]);
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.Output("[HGrid] Wrong format for link-region command: " + e.Message);
                        LinkRegionCmdUsage();
                        return;
                    }

                    // Convert cell coordinates given by the user to meters
                    xloc = xloc * (int)Constants.RegionSize;
                    yloc = yloc * (int)Constants.RegionSize;
                    string reason = string.Empty;
                    if (TryCreateLink(UUID.Zero, xloc, yloc, "", externalPort, externalHostName, out regInfo, out reason))
                    {
                        if (cmdparams.Length >= 5)
                        {
                            regInfo.RegionName = "";
                            for (int i = 4; i < cmdparams.Length; i++)
                                regInfo.RegionName += cmdparams[i] + " ";
                        }
                    }
                }
                return;
            }
            else if (command.Equals("unlink-region"))
            {
                if (cmdparams.Length < 1)
                {
                    UnlinkRegionCmdUsage();
                    return;
                }
                if (TryUnlinkRegion(cmdparams[0]))
                    MainConsole.Instance.Output("Successfully unlinked " + cmdparams[0]);
                else
                    MainConsole.Instance.Output("Unable to unlink " + cmdparams[0] + ", region not found.");
            }
        }

        private void LoadXmlLinkFile(string[] cmdparams)
        {
            //use http://www.hgurl.com/hypergrid.xml for test
            try
            {
                XmlReader r = XmlReader.Create(cmdparams[0]);
                XmlConfigSource cs = new XmlConfigSource(r);
                string[] excludeSections = null;

                if (cmdparams.Length == 2)
                {
                    if (cmdparams[1].ToLower().StartsWith("excludelist:"))
                    {
                        string excludeString = cmdparams[1].ToLower();
                        excludeString = excludeString.Remove(0, 12);
                        char[] splitter = { ';' };

                        excludeSections = excludeString.Split(splitter);
                    }
                }

                for (int i = 0; i < cs.Configs.Count; i++)
                {
                    bool skip = false;
                    if ((excludeSections != null) && (excludeSections.Length > 0))
                    {
                        for (int n = 0; n < excludeSections.Length; n++)
                        {
                            if (excludeSections[n] == cs.Configs[i].Name.ToLower())
                            {
                                skip = true;
                                break;
                            }
                        }
                    }
                    if (!skip)
                    {
                        ReadLinkFromConfig(cs.Configs[i]);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error(e.ToString());
            }
        }


        private void ReadLinkFromConfig(IConfig config)
        {
            GridRegion regInfo;
            int xloc, yloc;
            uint externalPort;
            string externalHostName;
            uint realXLoc, realYLoc;

            xloc = Convert.ToInt32(config.GetString("xloc", "0"));
            yloc = Convert.ToInt32(config.GetString("yloc", "0"));
            externalPort = Convert.ToUInt32(config.GetString("externalPort", "0"));
            externalHostName = config.GetString("externalHostName", "");
            realXLoc = Convert.ToUInt32(config.GetString("real-xloc", "0"));
            realYLoc = Convert.ToUInt32(config.GetString("real-yloc", "0"));

            if (m_enableAutoMapping)
            {
                xloc = (int)((xloc % 100) + m_autoMappingX);
                yloc = (int)((yloc % 100) + m_autoMappingY);
            }

            if (((realXLoc == 0) && (realYLoc == 0)) ||
                (((realXLoc - xloc < 3896) || (xloc - realXLoc < 3896)) &&
                 ((realYLoc - yloc < 3896) || (yloc - realYLoc < 3896))))
            {
                xloc = xloc * (int)Constants.RegionSize;
                yloc = yloc * (int)Constants.RegionSize;
                string reason = string.Empty;
                if (TryCreateLink(UUID.Zero, xloc, yloc, "", externalPort,
                                                     externalHostName, out regInfo, out reason))
                {
                    regInfo.RegionName = config.GetString("localName", "");
                }
                else
                    MainConsole.Instance.Output("Unable to link " + externalHostName + ": " + reason);
            }
        }


        private void LinkRegionCmdUsage()
        {
            MainConsole.Instance.Output("Usage: link-region <Xloc> <Yloc> <HostName>:<HttpPort>[:<RemoteRegionName>]");
            MainConsole.Instance.Output("Usage: link-region <Xloc> <Yloc> <HostName> <HttpPort> [<LocalName>]");
            MainConsole.Instance.Output("Usage: link-region <URI_of_xml> [<exclude>]");
        }

        private void UnlinkRegionCmdUsage()
        {
            MainConsole.Instance.Output("Usage: unlink-region <HostName>:<HttpPort>");
            MainConsole.Instance.Output("Usage: unlink-region <LocalName>");
        }

        #endregion

    }
}
