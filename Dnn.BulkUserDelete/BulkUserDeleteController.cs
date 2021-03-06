﻿using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DotNetNuke.Entities.Users;
using DotNetNuke.Web.Api;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Controllers;
using DotNetNuke.Services.Log.EventLog;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Services.FileSystem;
namespace Dnn.Modules.BulkUserDelete
{
    
    public class BulkUserDeleteController : DnnApiController
    {
        [HttpGet]
        [AllowAnonymous]
        public HttpResponseMessage AnonymousPing()
        {
            PingResult ping = new PingResult();
            ping.Success = true;
            ping.UserName = User.Identity.Name;
            ping.Message = "Bulk User Delete Anonymous Ping Return Result [" + User.Identity.Name + "]";
            ping.WasAuthorised = User.Identity.IsAuthenticated; 
            return Request.CreateResponse(HttpStatusCode.OK, ping);
        }
        [HttpGet]
        [DnnAuthorize(StaticRoles = "Administrators")]
        public HttpResponseMessage AdministratorPing()
        {
            PingResult ping = new PingResult();
            ping.Success = true;
            ping.Message = "Bulk User Delete Authenticated Ping Return Result [" + User.Identity.Name + "]";
            ping.StackTrace = null;
            ping.Error = null;
            ping.UserName = User.Identity.Name;
            ping.WasAuthorised = User.Identity.IsAuthenticated;
            return Request.CreateResponse(HttpStatusCode.OK, ping);
        }
        [HttpGet]
#if (DEBUG)
        [AllowAnonymous]
#else
        [DnnAuthorize(StaticRoles="Administrators")]
#endif
        public HttpResponseMessage GetSoftDeletedUsers(Newtonsoft.Json.Linq.JObject requestJson)
        {
            UserResult result = new UserResult();
            try
            {
                //return a summary of the number of deleted users
                if (this.PortalSettings == null) throw new ArgumentNullException("PortalSettings object");
                int portalId = this.PortalSettings.PortalId;
                UserController uc = new UserController();
                List<string> usernames = new List<string>();
                result.UsersAffected = 0;
                IDataReader reader = Data.SqlDataProvider.Instance().GetDeletedUsers(portalId);
                if (reader.Read())
                {
                    //total number of soft-deleted users
                    result.UsersRemaining = (int)reader[0];
                }
                if (reader.NextResult())
                {
                    //first ten usernames of soft-deleted users
                    while (reader.Read())
                    {
                        string username = (string)reader["UserName"];
                        int userId = (int)reader["UserId"];
                        UserInfo user = UserController.GetUser(portalId, userId, true);
                        result.UsersAffected++;
                        if (user != null)
                        {
                            string folderPath = ((PathUtils)PathUtils.Instance).GetUserFolderPath(user);
                            usernames.Add(username + " [" + userId.ToString() + "] - " + folderPath);
                        }
                        else
                            usernames.Add(username + " [" + userId.ToString() + "] - [no folder]");
                    }
                }
                
                result.Success = true;
                if (result.UsersRemaining > 0)
                {
                    //last ten soft-deleted usernames
                    if (result.UsersRemaining >= 10)
                    {
                        result.Message = "First ten soft-deleted users found:";
                    }
                    else
                    {
                        result.Message = "Found " + result.UsersAffected.ToString() + " Deleted Users:";
                    }
                    foreach (string user in usernames)
                        result.Message += user + ", ";
                }
                else
                    result.Message = "No Deleted Users Found";

                if (result.UsersRemaining > 10 && reader.NextResult())
                {
                    //count the last 10 users, but don't re-show existing users shown in first ten
                    int numberToCount = result.UsersRemaining - 10;
                    usernames = new List<string>();
                    for (int i = result.UsersAffected - 10; i <= result.UsersAffected; i++)
                    {
                        if (reader.Read())
                        {
                            usernames.Add((string)reader["UserName"]);
                            result.UsersAffected++;
                        }
                        else
                            break;
                    }

                    if (usernames.Count > 0)
                    {
                        result.Message += "\nLast Deleted Users: (";
                        foreach (string user in usernames)
                            result.Message += user + ", ";
                        result.Message += ")";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.StackTrace = ex.StackTrace;
            }
            finally
            {
                result.UserName = User.Identity.Name;
                result.WasAuthorised = User.Identity.IsAuthenticated;
            }
            return Request.CreateResponse(HttpStatusCode.OK, result);
        }
        /// <summary>
        /// Public service call to hard delete a batch of users
        /// </summary>
        /// <param name="requestJson">JSON request including the number of users to delete</param>
        /// <remarks>Note : only deletes those users already 'soft' deleted (marked 'isDeleted')</remarks>
        /// <returns></returns>
        [HttpPost]
#if (DEBUG)
        [AllowAnonymous]
#else
        [DnnAuthorize(StaticRoles="Administrators")]
#endif
        
        public HttpResponseMessage HardDeleteNextUsers(Newtonsoft.Json.Linq.JObject requestJson)
        {
            UserResult result = new UserResult();
            try
            {
                if (requestJson == null) throw new ArgumentNullException("requestJson");
                bool testRun = false; int actionNumber = 0; bool useFastDelete = false;
                bool.TryParse(requestJson["testRun"].ToString(), out testRun);
                int.TryParse(requestJson["actionNumber"].ToString(), out actionNumber);
                bool.TryParse(requestJson["useFastDelete"].ToString(), out useFastDelete);
                //return a summary of the number of deleted users
                if (this.PortalSettings == null) throw new ArgumentNullException("PortalSettings object");
                int portalId = this.PortalSettings.PortalId;
                string message = null; int deletedUserCount = -1; int remainingUsersCount = -1; List<string> elapsedTimes;
                //call the method which actually performs the batch delete
                result.Success = UserDeleteController.HardDeleteUsers(testRun, actionNumber, portalId, useFastDelete, out deletedUserCount, out remainingUsersCount, out message, out elapsedTimes);
                result.Message = message;
#if (DEBUG)
                //in debug mode, output an array of call times
                result.ElapsedTimes = elapsedTimes.ToArray();
#endif
                if (result.Success)
                {
                    result.UsersAffected = deletedUserCount;
                    result.UsersRemaining = remainingUsersCount;
                }
                else
                {
                    result.Message = "Failed : no changes made";
                    result.Error = message;
                    result.UsersAffected = 0;
                    result.UsersRemaining = remainingUsersCount;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.StackTrace = ex.StackTrace;
            }
            finally
            {
                result.UserName = User.Identity.Name;
                result.WasAuthorised = User.Identity.IsAuthenticated;
            }
            return Request.CreateResponse(HttpStatusCode.OK, result);
        }
    }
    #region private user deletion routines
    internal static class UserDeleteController
    {
        class UserToDelete
        {
            public int UserId { get; set; }
            public string Username { get; set; }
        }
        /// <param name="useFastDelete">Fast delete uses a direct delete of the folder record and the folder.  Only works if on the local file system.  If on folder providers, the delete must go through the FolderManager call, which is slower.</param>
        /// <param name="deletedUserCount">out parameter indicating how many users deleted</param>
        /// <param name="remainingUsersCount">out parameter indicating how many soft deleted users remain in this portal</param>
        /// <param name="message">Information message about what happened in the call</param>
        /// <param name="ets">Array of strings detailing the speed of the function calls</param>
        /// <returns></returns>
        internal static bool HardDeleteUsers(bool testRun, int actionNumber, int fromPortalId, bool useFastDelete, out int deletedUserCount, out int remainingUsersCount, out string message, out List<string> ets)
        {
            bool result = false;
            message = ""; deletedUserCount = 0; remainingUsersCount = -1;
            ets = new List<string>(); DateTime before;
            string userNameDeleting = null; int userDeleting = -1;
            //get the user that is calling this service for updating records
            var callingUser = UserController.GetCurrentUserInfo();
            int callingUserId = -1;
            if (callingUser != null)
                callingUserId = callingUser.UserID;

            IDataReader reader = null;
            try
            {
                var usersList = new List<UserToDelete>();
                before = DateTime.Now;
                //open a reader fo the next users to delete
                using (reader = Data.DataProvider.Instance().FindNextUsersToDelete(fromPortalId, actionNumber))
                {
                    while (reader.Read())
                    {
                        usersList.Add(new UserToDelete { UserId = (int)reader["UserId"], Username = (string)reader["Username"] });

                    }
                    if (reader.NextResult())
                    {
                        if (reader.Read())
                        {
                            /* get count of remaining users */
                            /* note this raw count is taken before the delete */
                            remainingUsersCount = (int)reader[0];
                            remainingUsersCount = remainingUsersCount - deletedUserCount;
                        }
                    }
                }
                //keep a snapshot of how long that took
                SnapshotEt("FindNextUsersToDelete", ref ets, before);

                //iterate the list of users to delete
                foreach (var toDeleteUser in usersList)
                {
                    message += "Deleting User ID [" + toDeleteUser.UserId.ToString() + "] ; Username [" + toDeleteUser.Username + "]";
                    //keep these values in case of error, helps to track down user-specific issues
                    userNameDeleting = toDeleteUser.Username;
                    userDeleting = toDeleteUser.UserId;
                    //find the user
                    before = DateTime.Now;
                    UserInfo user = UserController.GetUser(fromPortalId, toDeleteUser.UserId, true);
                    SnapshotEt("GetUser [" + toDeleteUser.UserId.ToString() + "]", ref ets, before);
                    if (user != null) //check, maybe running on different threads, maybe already gone?
                    {
                        //get the folder path
                        before = DateTime.Now;
                        string folderPath = ((PathUtils)PathUtils.Instance).GetUserFolderPath(user);
                        SnapshotEt("GetUserFolderPath", ref ets, before);
                        message += "  with folder [" + folderPath + "]" + Environment.NewLine;
                        //do we have that folder in the Folder manager?
                        before = DateTime.Now;
                        if (FolderManager.Instance.FolderExists(fromPortalId, folderPath))
                        {
                            SnapshotEt("FolderManager.FolderExists", ref ets, before);
                            FolderInfo folder = (FolderInfo)FolderManager.Instance.GetFolder(fromPortalId, folderPath);
                            if (folder != null)
                            {
                                before = DateTime.Now;
                                if (!testRun && !useFastDelete)
                                {
                                    //normal delete uses the folder manager to delete
                                    FolderManager.Instance.DeleteFolder(folder);
                                    SnapshotEt("FolderManager.DeleteFolder[" + folder.FolderPath + "]", ref ets, before);
                                }
                                if (useFastDelete)
                                {
                                    //fast delete deletes the folder directly using the local path and direct delete in the database
                                    FastFolderDelete(fromPortalId, callingUserId, folder, testRun);
                                    if (testRun)
                                        SnapshotEt("FolderManager.FastFolderDelete[" + folder.FolderPath + "] skipped due to test run", ref ets, before);
                                    else
                                        SnapshotEt("FolderManager.FastFolderDelete[" + folder.FolderPath + "]", ref ets, before);
                                }
                            }
                            //go up one level and see if that parent folder is empty now?
                            before = DateTime.Now;
                            CleanUpParentPath(folderPath, fromPortalId, testRun, useFastDelete, ref message);
                            SnapshotEt("CleanUpParentPath", ref ets, before);

                        }

                        //calling AspNetMembershipProvider.cs/RemoveUser
                        before = DateTime.Now;
                        bool removeOK = true;
                        if (!testRun)
                            removeOK = DotNetNuke.Security.Membership.MembershipProvider.Instance().RemoveUser(user);
                        SnapshotEt("RemoveUser [" + user.UserID + "]", ref ets, before);
                        if (removeOK)
                        {
                            //call the membership delete user as well, to completely remove hte user from the site
                            if (!testRun)
                                removeOK = DotNetNuke.Security.Membership.AspNetMembershipProvider.Instance().DeleteUser(user);
                        }
                        if (removeOK)
                        {
                            //user is gone from the database
                            deletedUserCount++;
                        }
                        result = true;
                    }
                }

                if (result == false)
                {
                    deletedUserCount = 0;
                }
            }
            catch (Exception ex)
            {
                if (userNameDeleting != null)
                    message += "Failed while deleting username [" + userNameDeleting + "], userId [" + userDeleting.ToString() + "]" + Environment.NewLine;
                message += "Exception Message: " + ex.Message;
                result = false;
            }
            finally
            {
                if (reader != null && !reader.IsClosed)
                {
                    reader.Close();
                    reader.Dispose();
                    reader = null;
                }
            }
            return result;
        }

        /// <summary>
        /// Provides a simplified folder deletion process that bypasses the folder provider code, doesn't refresh the cache and deletes using system.io
        /// </summary>
        /// <param name="fromPortalId"></param>
        /// <param name="userId"></param>
        /// <param name="folder"></param>
        /// <param name="isTestRun"></param>
        private static void FastFolderDelete(int fromPortalId, int userId,  FolderInfo folder, bool isTestRun)
        {
            /* alternate direct folder delete which bypasses some of the work done in the in-built DNN call */
            //directly delete database entry
            if (folder == null) throw new ArgumentNullException("folder is null");
            string folderPath = folder.FolderPath;
            if (string.IsNullOrEmpty(folderPath) == false)
            {
                //directly delete folder
                string actualFolderPath = PortalSettings.Current.HomeDirectoryMapPath + folder.FolderPath.Replace("/","\\");//get the physical path on the machine - assumes local storage and not folder provider
                if (Directory.Exists(actualFolderPath))
                {
                    //check for files in the directory
                    string[] files = Directory.GetFiles(actualFolderPath);
                    if (files.Length > 0)
                    {
                        //has files in it, delete those too
                        foreach (string filename in files)
                        {
                            if (!isTestRun)
                                File.Delete(filename);
                        }
                    }
                    //yes, it's here, physically delete the folder
                    if (!isTestRun) Directory.Delete(actualFolderPath);
                }

                //directly delete db path
                if (!isTestRun)
                    DotNetNuke.Data.DataProvider.Instance().DeleteFolder(fromPortalId, PathUtils.Instance.FormatFolderPath(folder.FolderPath));
                //update parent folder
                var parentFolderPath = folderPath.Substring(0, folderPath.Substring(0, folderPath.Length - 1).LastIndexOf("/", StringComparison.Ordinal) + 1);
                var pfldr = FolderManager.Instance.GetFolder(fromPortalId, parentFolderPath);
                if (!isTestRun)
                {
                    DotNetNuke.Data.DataProvider.Instance().UpdateFolder(pfldr.PortalID, pfldr.VersionGuid, pfldr.FolderID
                        , PathUtils.Instance.FormatFolderPath(pfldr.FolderPath)
                        , pfldr.StorageLocation, pfldr.MappedPath, pfldr.IsProtected, pfldr.IsCached, pfldr.LastUpdated
                        , userId, pfldr.FolderMappingID, pfldr.IsVersioned, pfldr.WorkflowID, pfldr.ParentID);
                }
            }

        }



        private static void CleanUpParentPath(string folderPath, int fromPortalId, bool testRun, bool fastDelete, ref string message)
        {
            //need to keep a copy of the relative parent path and the absolute parent folder path 
            string[] segments = folderPath.Split('/');
            //go back through the path backwards
            for (int i = segments.GetUpperBound(0) - 2; i >= 0; i--)
            {
                //go forwards up to that point and build the path name
                string parentPath = "";
                for (int j = 0; j <= i; j++)
                    parentPath += segments[j] + "/";  //that is now our parent path to check

                //fast delete does it direct, otherwise go through foldermanager
                if (fastDelete)
                {
                    string parentFilePath = PortalSettings.Current.HomeDirectoryMapPath + parentPath.Replace("/", "\\");
                    if (parentFilePath.EndsWith("\\"))
                        parentFilePath = parentFilePath.Substring(0, parentFilePath.Length - 1);//remove trailing slash
                    //get the list of subdirectories for this path (the folder should already be deleted here, so should be zero if it is empty)
                    var subDirectories = Directory.GetDirectories(parentFilePath);
                    if (subDirectories.Length < 1 || (testRun && subDirectories.Length == 1))
                    {
                        //no subdirectories - delete folder and physical folder
                        if (!testRun)
                            DotNetNuke.Data.DataProvider.Instance().DeleteFolder(fromPortalId, PathUtils.Instance.FormatFolderPath(parentPath));
                        //directly delete folder
                        if (Directory.Exists(parentFilePath))
                        {
                            //yes, it's here, physically delete the folder
                            if (!testRun) Directory.Delete(parentFilePath);
                            message += "Parent Folder has no subfolders and was fast deleted [" + parentPath + "]" + Environment.NewLine;
                        }

                    }
                    else
                    {
                        //has subdirectories, break
                        break;
                    }
                }
                else
                {
                    if (FolderManager.Instance.FolderExists(fromPortalId, parentPath))
                    {
                        //this folder is empty, delete it
                        var parentFolder = FolderManager.Instance.GetFolder(fromPortalId, parentPath);
                        if (parentFolder.HasChildren == false)
                        {
                            if (!testRun) FolderManager.Instance.DeleteFolder(parentFolder);
                            message += "Parent Folder has no subfolders and was deleted [" + parentPath + "]" + Environment.NewLine;
                        }
                        else
                            break;//if it has children, no need to check further
                    }
                }
            }
        }
        private static void SnapshotEt(string action, ref List<string> ets, DateTime before)
        {
            /* create a string with a before/after elapsed time snapshot*/
            const string etFormat = "{0} elapsed time : {1}";
            if (ets == null) ets = new List<string>();
            ets.Add(string.Format(etFormat, action, (DateTime.Now - before).ToString()));
        }
    }
    #endregion
}
