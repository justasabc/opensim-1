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
using System.Reflection;
using Nini.Config;
using OpenSim.Data;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Console;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using log4net;

namespace OpenSim.Services.UserAccountService
{
    public class UserAccountService : UserAccountServiceBase, IUserAccountService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static UserAccountService m_RootInstance;

        protected IGridService m_GridService;
        protected IAuthenticationService m_AuthenticationService;
        protected IPresenceService m_PresenceService;
        protected IInventoryService m_InventoryService;

        public UserAccountService(IConfigSource config)
            : base(config)
        {
            IConfig userConfig = config.Configs["UserAccountService"];
            if (userConfig == null)
                throw new Exception("No UserAccountService configuration");

            // In case there are several instances of this class in the same process,
            // the console commands are only registered for the root instance
            if (m_RootInstance == null)
            {
                m_RootInstance = this;
                string gridServiceDll = userConfig.GetString("GridService", string.Empty);
                if (gridServiceDll != string.Empty)
                    m_GridService = LoadPlugin<IGridService>(gridServiceDll, new Object[] { config });

                string authServiceDll = userConfig.GetString("AuthenticationService", string.Empty);
                if (authServiceDll != string.Empty)
                    m_AuthenticationService = LoadPlugin<IAuthenticationService>(authServiceDll, new Object[] { config });

                string presenceServiceDll = userConfig.GetString("PresenceService", string.Empty);
                if (presenceServiceDll != string.Empty)
                    m_PresenceService = LoadPlugin<IPresenceService>(presenceServiceDll, new Object[] { config });

                string invServiceDll = userConfig.GetString("InventoryService", string.Empty);
                if (invServiceDll != string.Empty)
                    m_InventoryService = LoadPlugin<IInventoryService>(invServiceDll, new Object[] { config });

                if (MainConsole.Instance != null)
                {
                    MainConsole.Instance.Commands.AddCommand("UserService", false,
                            "create user",
                            "create user [<first> [<last> [<pass> [<email>]]]]",
                            "Create a new user", HandleCreateUser);
                    MainConsole.Instance.Commands.AddCommand("UserService", false, "reset user password",
                            "reset user password [<first> [<last> [<password>]]]",
                            "Reset a user password", HandleResetUserPassword);
                }

            }

        }

        #region IUserAccountService

        public UserAccount GetUserAccount(UUID scopeID, string firstName,
                string lastName)
        {
            UserAccountData[] d;

            if (scopeID != UUID.Zero)
            {
                d = m_Database.Get(
                        new string[] { "ScopeID", "FirstName", "LastName" },
                        new string[] { scopeID.ToString(), firstName, lastName });
            }
            else
            {
                d = m_Database.Get(
                        new string[] { "FirstName", "LastName" },
                        new string[] { firstName, lastName });
            }

            if (d.Length < 1)
                return null;

            return MakeUserAccount(d[0]);
        }

        private UserAccount MakeUserAccount(UserAccountData d)
        {
            UserAccount u = new UserAccount();
            u.FirstName = d.FirstName;
            u.LastName = d.LastName;
            u.PrincipalID = d.PrincipalID;
            u.ScopeID = d.ScopeID;
            if (d.Data.ContainsKey("Email") && d.Data["Email"] != null)
                u.Email = d.Data["Email"].ToString();
            else
                u.Email = string.Empty;
            u.Created = Convert.ToInt32(d.Data["Created"].ToString());
            if (d.Data.ContainsKey("UserTitle") && d.Data["UserTitle"] != null)
                u.UserTitle = d.Data["UserTitle"].ToString();
            else
                u.UserTitle = string.Empty;

            if (d.Data.ContainsKey("ServiceURLs") && d.Data["ServiceURLs"] != null)
            {
                string[] URLs = d.Data["ServiceURLs"].ToString().Split(new char[] { ' ' });
                u.ServiceURLs = new Dictionary<string, object>();

                foreach (string url in URLs)
                {
                    string[] parts = url.Split(new char[] { '=' });

                    if (parts.Length != 2)
                        continue;

                    string name = System.Web.HttpUtility.UrlDecode(parts[0]);
                    string val = System.Web.HttpUtility.UrlDecode(parts[1]);

                    u.ServiceURLs[name] = val;
                }
            }
            else
                u.ServiceURLs = new Dictionary<string, object>();

            return u;
        }

        public UserAccount GetUserAccount(UUID scopeID, string email)
        {
            UserAccountData[] d;

            if (scopeID != UUID.Zero)
            {
                d = m_Database.Get(
                        new string[] { "ScopeID", "Email" },
                        new string[] { scopeID.ToString(), email });
            }
            else
            {
                d = m_Database.Get(
                        new string[] { "Email" },
                        new string[] { email });
            }

            if (d.Length < 1)
                return null;

            return MakeUserAccount(d[0]);
        }

        public UserAccount GetUserAccount(UUID scopeID, UUID principalID)
        {
            UserAccountData[] d;

            if (scopeID != UUID.Zero)
            {
                d = m_Database.Get(
                        new string[] { "ScopeID", "PrincipalID" },
                        new string[] { scopeID.ToString(), principalID.ToString() });
            }
            else
            {
                d = m_Database.Get(
                        new string[] { "PrincipalID" },
                        new string[] { principalID.ToString() });
            }

            if (d.Length < 1)
            {
                return null;
            }

            return MakeUserAccount(d[0]);
        }

        public bool StoreUserAccount(UserAccount data)
        {
            UserAccountData d = new UserAccountData();

            d.FirstName = data.FirstName;
            d.LastName = data.LastName;
            d.PrincipalID = data.PrincipalID;
            d.ScopeID = data.ScopeID;
            d.Data = new Dictionary<string, string>();
            d.Data["Email"] = data.Email;
            d.Data["Created"] = data.Created.ToString();

            List<string> parts = new List<string>();

            foreach (KeyValuePair<string, object> kvp in data.ServiceURLs)
            {
                string key = System.Web.HttpUtility.UrlEncode(kvp.Key);
                string val = System.Web.HttpUtility.UrlEncode(kvp.Value.ToString());
                parts.Add(key + "=" + val);
            }

            d.Data["ServiceURLs"] = string.Join(" ", parts.ToArray());

            return m_Database.Store(d);
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, string query)
        {
            UserAccountData[] d = m_Database.GetUsers(scopeID, query);

            if (d == null)
                return new List<UserAccount>();

            List<UserAccount> ret = new List<UserAccount>();

            foreach (UserAccountData data in d)
                ret.Add(MakeUserAccount(data));

            return ret;
        }

        #endregion

        #region Console commands
        /// <summary>
        /// Create a new user
        /// </summary>
        /// <param name="cmdparams">string array with parameters: firstname, lastname, password, locationX, locationY, email</param>
        protected void HandleCreateUser(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string password;
            string email;

            if (cmdparams.Length < 3)
                firstName = MainConsole.Instance.CmdPrompt("First name", "Default");
            else firstName = cmdparams[2];

            if (cmdparams.Length < 4)
                lastName = MainConsole.Instance.CmdPrompt("Last name", "User");
            else lastName = cmdparams[3];

            if (cmdparams.Length < 5)
                password = MainConsole.Instance.PasswdPrompt("Password");
            else password = cmdparams[4];

            if (cmdparams.Length < 6)
                email = MainConsole.Instance.CmdPrompt("Email", "");
            else email = cmdparams[5];

            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);
            if (null == account)
            {
                account = new UserAccount(UUID.Zero, firstName, lastName, email);
                if (account.ServiceURLs == null || (account.ServiceURLs != null && account.ServiceURLs.Count == 0))
                {
                    account.ServiceURLs = new Dictionary<string, object>();
                    account.ServiceURLs["HomeURI"] = string.Empty;
                    account.ServiceURLs["GatekeeperURI"] = string.Empty;
                    account.ServiceURLs["InventoryServerURI"] = string.Empty;
                    account.ServiceURLs["AssetServerURI"] = string.Empty;
                }

                if (StoreUserAccount(account))
                {
                    bool success = false;
                    if (m_AuthenticationService != null)
                        success = m_AuthenticationService.SetPassword(account.PrincipalID, password);
                    if (!success)
                        m_log.WarnFormat("[USER ACCOUNT SERVICE]: Unable to set password for account {0} {1}.",
                           firstName, lastName);

                    GridRegion home = null;
                    if (m_GridService != null)
                    {
                        List<GridRegion> defaultRegions = m_GridService.GetDefaultRegions(UUID.Zero);
                        if (defaultRegions != null && defaultRegions.Count >= 1)
                            home = defaultRegions[0];

                        if (m_PresenceService != null && home != null)
                            m_PresenceService.SetHomeLocation(account.PrincipalID.ToString(), home.RegionID, new Vector3(128, 128, 0), new Vector3(0, 1, 0));
                        else
                            m_log.WarnFormat("[USER ACCOUNT SERVICE]: Unable to set home for account {0} {1}.",
                               firstName, lastName);

                    }
                    else
                        m_log.WarnFormat("[USER ACCOUNT SERVICE]: Unable to retrieve home region for account {0} {1}.",
                           firstName, lastName);

                    if (m_InventoryService != null)
                        success = m_InventoryService.CreateUserInventory(account.PrincipalID);
                    if (!success)
                        m_log.WarnFormat("[USER ACCOUNT SERVICE]: Unable to create inventory for account {0} {1}.",
                           firstName, lastName);


                    m_log.InfoFormat("[USER ACCOUNT SERVICE]: Account {0} {1} created successfully", firstName, lastName);
                }
            }
            else
            {
                m_log.ErrorFormat("[USER ACCOUNT SERVICE]: A user with the name {0} {1} already exists!", firstName, lastName);
            }

        }

        protected void HandleResetUserPassword(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string newPassword;

            if (cmdparams.Length < 4)
                firstName = MainConsole.Instance.CmdPrompt("First name");
            else firstName = cmdparams[3];

            if (cmdparams.Length < 5)
                lastName = MainConsole.Instance.CmdPrompt("Last name");
            else lastName = cmdparams[4];

            if (cmdparams.Length < 6)
                newPassword = MainConsole.Instance.PasswdPrompt("New password");
            else newPassword = cmdparams[5];

            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);
            if (account == null)
                m_log.ErrorFormat("[USER ACCOUNT SERVICE]: No such user");

            bool success = false;
            if (m_AuthenticationService != null)
                success = m_AuthenticationService.SetPassword(account.PrincipalID, newPassword);
            if (!success)
                m_log.ErrorFormat("[USER ACCOUNT SERVICE]: Unable to reset password for account {0} {1}.",
                   firstName, lastName);
            else
                m_log.InfoFormat("[USER ACCOUNT SERVICE]: Password reset for user {0} {1}", firstName, lastName);
        }

        #endregion

    }
}
