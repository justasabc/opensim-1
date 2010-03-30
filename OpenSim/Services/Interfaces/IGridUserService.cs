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

namespace OpenSim.Services.Interfaces
{
    /// <summary>
    /// Records user information specific to a grid but which is not part of a user's account.
    /// </summary>
    public class GridUserInfo
    {
        public string UserID;
        public UUID HomeRegionID;
        public Vector3 HomePosition;
        public Vector3 HomeLookAt;

        public GridUserInfo() {}
        
        public GridUserInfo(Dictionary<string, object> kvp)
        {
            if (kvp.ContainsKey("UserID"))
                UserID = kvp["UserID"].ToString();
            if (kvp.ContainsKey("HomeRegionID"))
                UUID.TryParse(kvp["HomeRegionID"].ToString(), out HomeRegionID);
            if (kvp.ContainsKey("HomePosition"))
                Vector3.TryParse(kvp["HomePosition"].ToString(), out HomePosition);
            if (kvp.ContainsKey("HomeLookAt"))
                Vector3.TryParse(kvp["HomeLookAt"].ToString(), out HomeLookAt);
        }

        public Dictionary<string, object> ToKeyValuePairs()
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["UserID"] = UserID;
            result["HomeRegionID"] = HomeRegionID.ToString();
            result["HomePosition"] = HomePosition.ToString();
            result["HomeLookAt"] = HomeLookAt.ToString();

            return result;
        }
    }
    
    public interface IGridUserService
    {
        GridUserInfo GetGridUserInfo(string userID);
        bool StoreGridUserInfo(GridUserInfo info);
    }
}