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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Simulation;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using log4net;
using Nwc.XmlRpc;
using Nini.Config;

namespace OpenSim.Services.Connectors.Hypergrid
{
    public class UserAgentServiceConnector : IUserAgentService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        string m_ServerURL;
        public UserAgentServiceConnector(string url)
        {
            m_ServerURL = url;
        }

        public UserAgentServiceConnector(IConfigSource config)
        {
        }

        public bool LoginAgentToGrid(AgentCircuitData aCircuit, GridRegion gatekeeper, GridRegion destination, out string reason)
        {
            reason = String.Empty;

            if (destination == null)
            {
                reason = "Destination is null";
                m_log.Debug("[USER AGENT CONNECTOR]: Given destination is null");
                return false;
            }

            string uri =  m_ServerURL + "/homeagent/" + aCircuit.AgentID + "/";

            Console.WriteLine("   >>> LoginAgentToGrid <<< " + uri);

            HttpWebRequest AgentCreateRequest = (HttpWebRequest)WebRequest.Create(uri);
            AgentCreateRequest.Method = "POST";
            AgentCreateRequest.ContentType = "application/json";
            AgentCreateRequest.Timeout = 10000;
            //AgentCreateRequest.KeepAlive = false;
            //AgentCreateRequest.Headers.Add("Authorization", authKey);

            // Fill it in
            OSDMap args = PackCreateAgentArguments(aCircuit, gatekeeper, destination);

            string strBuffer = "";
            byte[] buffer = new byte[1];
            try
            {
                strBuffer = OSDParser.SerializeJsonString(args);
                Encoding str = Util.UTF8;
                buffer = str.GetBytes(strBuffer);

            }
            catch (Exception e)
            {
                m_log.WarnFormat("[USER AGENT CONNECTOR]: Exception thrown on serialization of ChildCreate: {0}", e.Message);
                // ignore. buffer will be empty, caller should check.
            }

            Stream os = null;
            try
            { // send the Post
                AgentCreateRequest.ContentLength = buffer.Length;   //Count bytes to send
                os = AgentCreateRequest.GetRequestStream();
                os.Write(buffer, 0, strBuffer.Length);         //Send it
                m_log.InfoFormat("[USER AGENT CONNECTOR]: Posted CreateAgent request to remote sim {0}, region {1}, x={2} y={3}",
                    uri, destination.RegionName, destination.RegionLocX, destination.RegionLocY);
            }
            //catch (WebException ex)
            catch
            {
                //m_log.InfoFormat("[USER AGENT CONNECTOR]: Bad send on ChildAgentUpdate {0}", ex.Message);
                reason = "cannot contact remote region";
                return false;
            }
            finally
            {
                if (os != null)
                    os.Close();
            }

            // Let's wait for the response
            //m_log.Info("[USER AGENT CONNECTOR]: Waiting for a reply after DoCreateChildAgentCall");

            WebResponse webResponse = null;
            StreamReader sr = null;
            try
            {
                webResponse = AgentCreateRequest.GetResponse();
                if (webResponse == null)
                {
                    m_log.Info("[USER AGENT CONNECTOR]: Null reply on DoCreateChildAgentCall post");
                }
                else
                {

                    sr = new StreamReader(webResponse.GetResponseStream());
                    string response = sr.ReadToEnd().Trim();
                    m_log.InfoFormat("[USER AGENT CONNECTOR]: DoCreateChildAgentCall reply was {0} ", response);

                    if (!String.IsNullOrEmpty(response))
                    {
                        try
                        {
                            // we assume we got an OSDMap back
                            OSDMap r = Util.GetOSDMap(response);
                            bool success = r["success"].AsBoolean();
                            reason = r["reason"].AsString();
                            return success;
                        }
                        catch (NullReferenceException e)
                        {
                            m_log.InfoFormat("[USER AGENT CONNECTOR]: exception on reply of DoCreateChildAgentCall {0}", e.Message);

                            // check for old style response
                            if (response.ToLower().StartsWith("true"))
                                return true;

                            return false;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                m_log.InfoFormat("[USER AGENT CONNECTOR]: exception on reply of DoCreateChildAgentCall {0}", ex.Message);
                reason = "Destination did not reply";
                return false;
            }
            finally
            {
                if (sr != null)
                    sr.Close();
            }

            return true;

        }

        protected OSDMap PackCreateAgentArguments(AgentCircuitData aCircuit, GridRegion gatekeeper, GridRegion destination)
        {
            OSDMap args = null;
            try
            {
                args = aCircuit.PackAgentCircuitData();
            }
            catch (Exception e)
            {
                m_log.Debug("[USER AGENT CONNECTOR]: PackAgentCircuitData failed with exception: " + e.Message);
            }
            // Add the input arguments
            args["gatekeeper_host"] = OSD.FromString(gatekeeper.ExternalHostName);
            args["gatekeeper_port"] = OSD.FromString(gatekeeper.HttpPort.ToString());
            args["destination_x"] = OSD.FromString(destination.RegionLocX.ToString());
            args["destination_y"] = OSD.FromString(destination.RegionLocY.ToString());
            args["destination_name"] = OSD.FromString(destination.RegionName);
            args["destination_uuid"] = OSD.FromString(destination.RegionID.ToString());

            return args;
        }

        public GridRegion GetHomeRegion(UUID userID, out Vector3 position, out Vector3 lookAt)
        {
            position = Vector3.UnitY; lookAt = Vector3.UnitY;

            Hashtable hash = new Hashtable();
            hash["userID"] = userID.ToString();

            IList paramList = new ArrayList();
            paramList.Add(hash);

            XmlRpcRequest request = new XmlRpcRequest("get_home_region", paramList);
            XmlRpcResponse response = null;
            try
            {
                response = request.Send(m_ServerURL, 10000);
            }
            catch (Exception e)
            {
                return null;
            }

            if (response.IsFault)
            {
                return null;
            }

            hash = (Hashtable)response.Value;
            //foreach (Object o in hash)
            //    m_log.Debug(">> " + ((DictionaryEntry)o).Key + ":" + ((DictionaryEntry)o).Value);
            try
            {
                bool success = false;
                Boolean.TryParse((string)hash["result"], out success);
                if (success)
                {
                    GridRegion region = new GridRegion();

                    UUID.TryParse((string)hash["uuid"], out region.RegionID);
                    //m_log.Debug(">> HERE, uuid: " + region.RegionID);
                    int n = 0;
                    if (hash["x"] != null)
                    {
                        Int32.TryParse((string)hash["x"], out n);
                        region.RegionLocX = n;
                        //m_log.Debug(">> HERE, x: " + region.RegionLocX);
                    }
                    if (hash["y"] != null)
                    {
                        Int32.TryParse((string)hash["y"], out n);
                        region.RegionLocY = n;
                        //m_log.Debug(">> HERE, y: " + region.RegionLocY);
                    }
                    if (hash["region_name"] != null)
                    {
                        region.RegionName = (string)hash["region_name"];
                        //m_log.Debug(">> HERE, name: " + region.RegionName);
                    }
                    if (hash["hostname"] != null)
                        region.ExternalHostName = (string)hash["hostname"];
                    if (hash["http_port"] != null)
                    {
                        uint p = 0;
                        UInt32.TryParse((string)hash["http_port"], out p);
                        region.HttpPort = p;
                    }
                    if (hash["internal_port"] != null)
                    {
                        int p = 0;
                        Int32.TryParse((string)hash["internal_port"], out p);
                        region.InternalEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), p);
                    }
                    if (hash["position"] != null)
                        Vector3.TryParse((string)hash["position"], out position);
                    if (hash["lookAt"] != null)
                        Vector3.TryParse((string)hash["lookAt"], out lookAt);

                    // Successful return
                    return region;
                }

            }
            catch (Exception e)
            {
                return null;
            }

            return null;

        }

        public bool AgentIsComingHome(UUID sessionID, string thisGridExternalName)
        {
            Hashtable hash = new Hashtable();
            hash["sessionID"] = sessionID.ToString();
            hash["externalName"] = thisGridExternalName;

            IList paramList = new ArrayList();
            paramList.Add(hash);

            XmlRpcRequest request = new XmlRpcRequest("agent_is_coming_home", paramList);
            string reason = string.Empty;
            return GetBoolResponse(request, out reason);
        }

        public bool VerifyAgent(UUID sessionID, string token)
        {
            Hashtable hash = new Hashtable();
            hash["sessionID"] = sessionID.ToString();
            hash["token"] = token;

            IList paramList = new ArrayList();
            paramList.Add(hash);

            XmlRpcRequest request = new XmlRpcRequest("verify_agent", paramList);
            string reason = string.Empty;
            return GetBoolResponse(request, out reason);
        }

        public bool VerifyClient(UUID sessionID, string token)
        {
            Hashtable hash = new Hashtable();
            hash["sessionID"] = sessionID.ToString();
            hash["token"] = token;

            IList paramList = new ArrayList();
            paramList.Add(hash);

            XmlRpcRequest request = new XmlRpcRequest("verify_client", paramList);
            string reason = string.Empty;
            return GetBoolResponse(request, out reason);
        }

        public void LogoutAgent(UUID userID, UUID sessionID)
        {
            Hashtable hash = new Hashtable();
            hash["sessionID"] = sessionID.ToString();
            hash["userID"] = userID.ToString();

            IList paramList = new ArrayList();
            paramList.Add(hash);

            XmlRpcRequest request = new XmlRpcRequest("logout_agent", paramList);
            string reason = string.Empty;
            GetBoolResponse(request, out reason);
        }


        private bool GetBoolResponse(XmlRpcRequest request, out string reason)
        {
            //m_log.Debug("[HGrid]: Linking to " + uri);
            XmlRpcResponse response = null;
            try
            {
                response = request.Send(m_ServerURL, 10000);
            }
            catch (Exception e)
            {
                m_log.Debug("[USER AGENT CONNECTOR]: Unable to contact remote server ");
                reason = "Exception: " + e.Message;
                return false;
            }

            if (response.IsFault)
            {
                m_log.ErrorFormat("[HGrid]: remote call returned an error: {0}", response.FaultString);
                reason = "XMLRPC Fault";
                return false;
            }

            Hashtable hash = (Hashtable)response.Value;
            //foreach (Object o in hash)
            //    m_log.Debug(">> " + ((DictionaryEntry)o).Key + ":" + ((DictionaryEntry)o).Value);
            try
            {
                bool success = false;
                reason = string.Empty;
                Boolean.TryParse((string)hash["result"], out success);

                return success;
            }
            catch (Exception e)
            {
                m_log.Error("[HGrid]: Got exception while parsing GetEndPoint response " + e.StackTrace);
                reason = "Exception: " + e.Message;
                return false;
            }

        }

    }
}
