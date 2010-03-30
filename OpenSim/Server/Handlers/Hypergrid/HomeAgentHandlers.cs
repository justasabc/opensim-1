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
using System.Collections;
using System.IO;
using System.Reflection;
using System.Net;
using System.Text;

using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Simulation;
using Utils = OpenSim.Server.Handlers.Simulation.Utils;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Nini.Config;
using log4net;


namespace OpenSim.Server.Handlers.Hypergrid
{
    public class HomeAgentHandler 
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private IUserAgentService m_UserAgentService;

        public HomeAgentHandler(IUserAgentService userAgentService)
        {
            m_UserAgentService = userAgentService;
        }

        public Hashtable Handler(Hashtable request)
        {
            m_log.Debug("[CONNECTION DEBUGGING]: HomeAgentHandler Called");

            m_log.Debug("---------------------------");
            m_log.Debug(" >> uri=" + request["uri"]);
            m_log.Debug(" >> content-type=" + request["content-type"]);
            m_log.Debug(" >> http-method=" + request["http-method"]);
            m_log.Debug("---------------------------\n");

            Hashtable responsedata = new Hashtable();
            responsedata["content_type"] = "text/html";
            responsedata["keepalive"] = false;


            UUID agentID;
            UUID regionID;
            string action;
            if (!Utils.GetParams((string)request["uri"], out agentID, out regionID, out action))
            {
                m_log.InfoFormat("[HOME AGENT HANDLER]: Invalid parameters for agent message {0}", request["uri"]);
                responsedata["int_response_code"] = 404;
                responsedata["str_response_string"] = "false";

                return responsedata;
            }

            // Next, let's parse the verb
            string method = (string)request["http-method"];
            if (method.Equals("POST"))
            {
                DoAgentPost(request, responsedata, agentID);
                return responsedata;
            }
            else
            {
                m_log.InfoFormat("[HOME AGENT HANDLER]: method {0} not supported in agent message", method);
                responsedata["int_response_code"] = HttpStatusCode.MethodNotAllowed;
                responsedata["str_response_string"] = "Method not allowed";

                return responsedata;
            }

        }

        protected void DoAgentPost(Hashtable request, Hashtable responsedata, UUID id)
        {
            OSDMap args = Utils.GetOSDMap((string)request["body"]);
            if (args == null)
            {
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            // retrieve the input arguments
            int x = 0, y = 0;
            UUID uuid = UUID.Zero;
            string regionname = string.Empty;
            string gatekeeper_host = string.Empty;
            int gatekeeper_port = 0;

            if (args.ContainsKey("gatekeeper_host") && args["gatekeeper_host"] != null)
                gatekeeper_host = args["gatekeeper_host"].AsString();
            if (args.ContainsKey("gatekeeper_port") && args["gatekeeper_port"] != null)
                Int32.TryParse(args["gatekeeper_port"].AsString(), out gatekeeper_port);

            GridRegion gatekeeper = new GridRegion();
            gatekeeper.ExternalHostName = gatekeeper_host;
            gatekeeper.HttpPort = (uint)gatekeeper_port;
            gatekeeper.InternalEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0);

            if (args.ContainsKey("destination_x") && args["destination_x"] != null)
                Int32.TryParse(args["destination_x"].AsString(), out x);
            else
                m_log.WarnFormat("  -- request didn't have destination_x");
            if (args.ContainsKey("destination_y") && args["destination_y"] != null)
                Int32.TryParse(args["destination_y"].AsString(), out y);
            else
                m_log.WarnFormat("  -- request didn't have destination_y");
            if (args.ContainsKey("destination_uuid") && args["destination_uuid"] != null)
                UUID.TryParse(args["destination_uuid"].AsString(), out uuid);
            if (args.ContainsKey("destination_name") && args["destination_name"] != null)
                regionname = args["destination_name"].ToString();

            GridRegion destination = new GridRegion();
            destination.RegionID = uuid;
            destination.RegionLocX = x;
            destination.RegionLocY = y;
            destination.RegionName = regionname;

            AgentCircuitData aCircuit = new AgentCircuitData();
            try
            {
                aCircuit.UnpackAgentCircuitData(args);
            }
            catch (Exception ex)
            {
                m_log.InfoFormat("[HOME AGENT HANDLER]: exception on unpacking ChildCreate message {0}", ex.Message);
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            OSDMap resp = new OSDMap(2);
            string reason = String.Empty;

            bool result = m_UserAgentService.LoginAgentToGrid(aCircuit, gatekeeper, destination, out reason);

            resp["reason"] = OSD.FromString(reason);
            resp["success"] = OSD.FromBoolean(result);

            // TODO: add reason if not String.Empty?
            responsedata["int_response_code"] = HttpStatusCode.OK;
            responsedata["str_response_string"] = OSDParser.SerializeJsonString(resp);
        }

    }

}
