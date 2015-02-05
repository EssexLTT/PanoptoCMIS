using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace CMISImporter
{
    class Program
    {
        #region Global fields
        /// <summary>
        /// How many results per page should we retrieve from Panopto?
        /// </summary>
        static readonly int iResultsPerPage = 25;


        static bool bCertificateInitialized = false;
        #endregion

        static void Main(string[] args)
        {
            // How far ahead should we look in CMIS Data?
            int iLookAheadHours = 24;

            //Test scripts
            //GetRecorder("S11446");
            //GetFolder("AAAAATEST");
            //GetUser("BENTEST");

            //Rather than run everything here, call Process() to handle the CMIS import
            Process(iLookAheadHours);
        }


        private static void Process(int iLookAheadHours)
        {
            //Get the CMIS timetable for the next ?? hours
            IEnumerable<DataRow> dtTimetable = GetTimetable(iLookAheadHours);

            //Iterate through each event in the timetable
            foreach (DataRow drEvent in dtTimetable)
            {
                //Try/Catch. If this particular event has problems, we probably want the loop to continue
                try
                {
                    //Firstly, check there is a remote recorder in this location
                    RemoteRecorderManagement.RemoteRecorder rRecorder = GetRecorder(drEvent.Field<string>("RoomId"));

                    //If we have a recorder, then we can record this room/event
                    if (rRecorder != null)
                    {
                        //Get/create the folder for this recording
                        SessionManagement.Folder fFolder = GetFolder(drEvent.Field<string>("LecturerIds"));

                        //Ensure that the owner of this recording has access to said folder
                        //The data from CMIS stores multiple lecturers as CSV data inside the LecturerIds field. eg. LECT001, LECT002, LECT003
                        //Ideally, we would split this into an array and ensure that each lecturer had creator access on the folder
                        foreach (string sUser in drEvent.Field<string>("LecturerIds").Split(','))
                        {
                            //Another try/catch block - Panopto will throw an exception if user doesn't exist or already has permission
                            try
                            {
                                //Get/create the user
                                UserManagement.User uUser = GetUser(sUser);

                                //Create AccessManagement Client
                                AccessManagement.AccessManagementClient AMC = new AccessManagement.AccessManagementClient();
                                //Give user permission on our folder
                                AMC.GrantUsersAccessToFolder(AccessAuthentication(), fFolder.Id, new Guid[] { uUser.UserId }, AccessManagement.AccessRole.Creator);
                            } catch (Exception ex)
                            {
                                //Do nothing
                            }
                        }

                        //Now, we schedule the recording
                        //Create the RemoteRecorderManagement client
                        RemoteRecorderManagement.RemoteRecorderManagementClient RMC = new RemoteRecorderManagement.RemoteRecorderManagementClient();

                        //Name this recording
                        string sName = drEvent.Field<string>("ModuleIds") + " - " + drEvent.Field<string>("RoomId") + " - " + drEvent.Field<DateTime>("StartTime"); //Module, Location, Time
                        //Build the RemoteRecorderSettings object:
                        RemoteRecorderManagement.RecorderSettings RecorderSetting = new RemoteRecorderManagement.RecorderSettings()
                        {
                            RecorderId = rRecorder.Id
                        };
                        //Schedule the recording
                        //Note that we trim 1 minute from the end of the recording to give the remoterecorder time to recover between back-to-back recordings
                        RemoteRecorderManagement.ScheduledRecordingResult RemoteRecorderResult = RMC.ScheduleRecording(RemoteRecorderAuthentication(), sName, fFolder.Id, false, drEvent.Field<DateTime>("StartTime").ToUniversalTime(), drEvent.Field<DateTime>("StartTime").ToUniversalTime().AddMinutes(-1), new RemoteRecorderManagement.RecorderSettings[] { RecorderSetting });
                        
                        //Check that the event was scheduled properly
                        if (!RemoteRecorderResult.ConflictsExist)
                        {
                            //Nothing clashed

                            //We're only adding a single schedule, so can grab first DeliveryID
                            Guid gDeliveryIGuid = RemoteRecorderResult.SessionIDs.FirstOrDefault(); 
                            //At this point you could store the Guid in a database somewhere, to keep track of Panopto sessions and CMIS events
                        } 
                        else 
                        {
                            //The schedule was not created because it clashes with an existing schedule on this remote recorder
                            //Our new schedule has not been added to Panopto
                            //RemoteRecorderResult.ConflictingSessions will tell us the DeliveryIDs of the existing sessions
                        }                       
                    };
                } 
                catch (Exception ex)
                {
                    //Something happened whilst retrieving the remoterecorder, creating the folder and user, assigning permissions, or scheduling the recording
                    //Deal with this problem as you deem appropriate
                }
            }
        }

        #region CMIS
        private static IEnumerable<DataRow> GetTimetable(int iHoursAhead)
        {
            //Local variables
            IEnumerable<DataRow> ieData;

            using (SqlConnection SQLConn = new SqlConnection(ConfigurationManager.ConnectionStrings["CMISDB"].ConnectionString))
            {
                //Create table to use in a bit
                DataTable dtData = new DataTable();
                
                //Open data connection
                SQLConn.Open();

                //Make query
                SqlCommand SQLComm = SQLConn.CreateCommand();
                SQLComm.CommandText = "SELECT * FROM cmis_web.dbo.v_GetAllEventsUniqueRoom"; //See v_GetAllEventsByRoom.sql, misname between SQL server and code
                //Data adapter
                SqlDataAdapter SQLAdapt = new SqlDataAdapter(SQLComm);
                SQLAdapt.Fill(dtData);
                //Push into IEnumerable
                ieData = dtData.AsEnumerable();

                //Close connection
                SQLConn.Close();
                SQLConn.Dispose();
            }
            
            //Limit to next ?? hours
            ieData = ieData.Where(t => t.Field<DateTime>("StartTime").ToUniversalTime() >= DateTime.Now.ToUniversalTime() && t.Field<DateTime>("StartTime").ToUniversalTime() <= DateTime.Now.AddHours(iHoursAhead).ToUniversalTime()).AsEnumerable();
            return ieData;
        }
        #endregion

        #region Panopto
        //A quick note on the following methods:
        //These all assume that you are adding 1-2 events from CMIS to Panopto.
        //It's likely that in the real-world, you'll be adding 100+ events in one go. This can have pretty horrific performance issues with all the required API calls.
        //In a lot of cases it is far easier to cache some lookups locally and refer to them instead (eg. a list of remote recorders). The information is relatively static and this is safe to do.
        //None of this example code uses caching.

        /// <summary>
        /// Finds a Panopto Remote Recorder by name (description)
        /// </summary>
        private static RemoteRecorderManagement.RemoteRecorder GetRecorder(string sLocation)
        {
            //Local variables
            string sRemoteRecorderName;

            //This method could be used to translate between rooms and remote recorder names.
            //To keep things simple in this example, we'll assume that the remote recorder name is the same as the room name (eg. remote recorder 'LTB06' is located in room 'LTB06')

            //But there are several different ways to handle remote recorder names:
            //- Name your remote recorders after the location they are installed in (whcih is what we'll do here)
            //- Use a lookup table to determine where your remote recorders are located (University of Essex does this)
            //- Use the ExternalId property of the RemoteRecorder object to store a custom ID for each recorder

            //LOOKUP CODE GOES HERE (as above, for brevity we'll assume the remote recorder's name (description) is the same as its location            
            sRemoteRecorderName = sLocation;

            //Pagination isn't strictly necessary as we've increased maxReceivedMessageSize="6553560" in the app.config
            //But we'll keep the pagination code for sake of completeness
            RemoteRecorderManagement.Pagination RemoteRecorderPagination = new RemoteRecorderManagement.Pagination()
                                                             {
                                                                 MaxNumberResults = iResultsPerPage,
                                                                 PageNumber = 0
                                                             };

            //Create a Remote Recorder Management Client
            RemoteRecorderManagement.RemoteRecorderManagementClient RemoteRecorderClient = new RemoteRecorderManagement.RemoteRecorderManagementClient();

            //Lookup all Remote Recorders
            //Note: if you had stored the RecorderId or ExternalId elsewhere, you could use the RemoteRecorderClient.GetRecordersById or RemoteRecorderClient.GetRecordersByExternalID methods to return a single recorder
            //Note 2: you could potentially cache this response in a global object and check/use it in subsequent calls to speed up the code
            RemoteRecorderManagement.ListRecordersResponse RemoteRecorderResponse = RemoteRecorderClient.ListRecorders(RemoteRecorderAuthentication(), RemoteRecorderPagination, RemoteRecorderManagement.RecorderSortField.Name);

            #region ListRecorders handling
            //Loop through the returned pages, add each result to a List
            //If we wanted to speed up this code we could potentially stop at the first match, but that carries risk of ignoring duplicate remote recorders
            List<RemoteRecorderManagement.RemoteRecorder> lRecorders = new List<RemoteRecorderManagement.RemoteRecorder>();

            //Keep track of the number of items remaining in the RemoteRecorderResponse queue
            int iTotalPages = (int)Math.Ceiling(((double)RemoteRecorderResponse.TotalResultCount / (double)iResultsPerPage)); //Total number of pages (total items/items per page) 
            int iCurrentPage = 0;

            while (iCurrentPage < iTotalPages)
            {
                //Increment the page number
                iCurrentPage++;

                //Grab results from this age and add to list
                lRecorders.AddRange(RemoteRecorderResponse.PagedResults.AsEnumerable());

                //If we still have items outstanding, we should grab the next page
                if (iCurrentPage < iTotalPages)
                {
                    RemoteRecorderPagination = new RemoteRecorderManagement.Pagination()
                                               {
                                                   //Keep the same number of items per page as before
                                                   MaxNumberResults = iResultsPerPage,                                                   
                                                   PageNumber = iCurrentPage
                                               };
                    //Grab the next page from Panopto's API
                    RemoteRecorderResponse = RemoteRecorderClient.ListRecorders(RemoteRecorderAuthentication(), RemoteRecorderPagination, RemoteRecorderManagement.RecorderSortField.Name);
                };
            }

            //Narrow down our list of recorders to match the one we're after
            lRecorders = lRecorders.Where(r => r.Name.ToLower().Trim() == sRemoteRecorderName.Trim().ToLower()).ToList();

            //Duplicate matches?
            if (lRecorders.Count > 1)
                throw new Exception("More than one remote recorder found: " + sRemoteRecorderName);
            else if (lRecorders.Count == 0)
                return null;
            else
                return lRecorders.FirstOrDefault();
            #endregion           
        }

        /// <summary>
        /// Finds a Panopto folder by name (creates it if it does not already exist)
        /// </summary>
        private static SessionManagement.Folder GetFolder(string sFolderName)
        {
            //As with RemoteRecorder, pagination isn't strictly necessary as we've increased maxReceivedMessageSize="6553560" in the app.config
            //It's also unlikely that we'll return more than 25 results, since ListFolders method allows searching.
            SessionManagement.Pagination FolderPagination = new SessionManagement.Pagination()
            {
                MaxNumberResults = 1, //We only want a single folder
                PageNumber = 0
            };
            //ListFolders requires a request object
            SessionManagement.ListFoldersRequest FolderRequest = new SessionManagement.ListFoldersRequest()
            {
                 Pagination = FolderPagination,
                 ParentFolderId = Guid.Empty, //Assume we're searching from the top of the folder hierarchy
                 PublicOnly = false, //Assume we're searching all folders
                 SortBy = SessionManagement.FolderSortField.Relavance, //Relevance looks for the closest match folder (eg. search for foo = foo before foobar)
                 SortIncreasing = true
            };

            //Create a SessionManagement Client
            SessionManagement.SessionManagementClient SMC = new SessionManagement.SessionManagementClient();

            //List folders
            SessionManagement.ListFoldersResponse FolderResponse = SMC.GetFoldersList(SessionAuthentication(), FolderRequest, sFolderName);
            //Note: in v4.6.1 we currently experience a bug where no folders are found, even though one exists YMMV

            //Note, as mentioned above, we use the search parameter to narrow down our list of folders before they are returned via the API
            //It's highly unlikely that you''ll have more than one folder with the same name, so we don't need to worry about pagination

            //Check the number of folders
            if (FolderResponse.Results.Count() == 0)
                //It doesn't look like aFolderName exists in Panopto, let's create it
                //We're going to assume that this is a public folder at the top level of our hierarchy
                //Folder is public because it massively simplifies permissions. Essex actually makes it private and then gives viewer access on each recording individually.
                return SMC.AddFolder(SessionAuthentication(), sFolderName, Guid.Empty, true); 
            else if (FolderResponse.Results.Count() > 1)
                throw new Exception("More than one folder found: " + sFolderName);
            else 
                return FolderResponse.Results.FirstOrDefault();
        }

        /// <summary>
        /// Finds a Panopto user by username (creates them if they do not already exist)
        /// </summary>
        private static UserManagement.User GetUser(string sUserID)
        {
            //Note: Panopto supports multiple identity providers (IDP). 
            //In this example code, we are going to assume that our users are stored in Active Directory (AD)

            //Identity Provider/Domain
            string sIDP = "essex.ac.uk//";

            //Local variables
            UserManagement.User uPanopto;
            string sUsername;

            //UserID depends on how you are storing Lecturer identities in CMIS. This could be their AD login, StaffID, or something else.
            //Essex gives staff a uniqueID (not their AD login), so we have to lookup each user.
            //For brevity, we'll just assume username = userID
            sUsername = sUserID; //ie. CMIS LecturerIDs are the same as AD logins

            //Create UserManagement Client
            UserManagement.UserManagementClient UMC = new UserManagement.UserManagementClient();

            //We are looking for an individual user by username, Panopto provides a quick method to look this up
            uPanopto = UMC.GetUserByKey(UserAuthentication(), sIDP + sUsername);

            //Check if the returned user object is null. If it it, means user doesn't exist in Panopto.
            if (uPanopto == null)
            {
                //I'm going to be lazy and not duplicate the code used to grab a user from Active Directory
                //There's an excellent guide on importing users from AD into Panopto at http://www.mediaguy.co.uk/panopto-api/panopto-api-301-creating-users-and-groups-from-ad/

                //For this example we will just assume everyone is called Foo Bar
                uPanopto = new UserManagement.User()
                {
                    UserKey = sIDP + sUsername,
                    FirstName = "Foo",
                    LastName = "Bar",
                    Email = sUsername + "@essex.ac.uk",
                    EmailSessionNotifications = false,
                    SystemRole = UserManagement.SystemRole.None,
                    UserBio = String.Empty,                       
                };

                //Add the user to Panopto, get back the Guid of the newly added user
                Guid gUser = UMC.CreateUser(UserAuthentication(), uPanopto, String.Empty); //No need for password as user is authenticating via AD

                //Finally, add the now-known guid to our user object and return
                uPanopto.UserId = gUser;
                return uPanopto;
            } 
            else 
            {
                //We have the user
                return uPanopto;
            }
        }
        #endregion

        #region Panopto Authentication
        /// <summary>
        /// Ensures that our custom certificate validation has been applied
        /// </summary>
        public static void EnsureCertificateValidation()
        {
            if (!bCertificateInitialized)
            {
                ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(CustomCertificateValidation);
                bCertificateInitialized = true;
            }
        }

        /// <summary>
        /// Ensures that server certificate is authenticated
        /// </summary>
        private static bool CustomCertificateValidation(object sender, X509Certificate cert, X509Chain chain, System.Net.Security.SslPolicyErrors error)
        {
            return true;
        }

        /// <summary>
        /// Reads Panopto authentication information from app.settings (technically appsettings.config)
        /// </summary>
        private static RemoteRecorderManagement.AuthenticationInfo RemoteRecorderAuthentication()
        {
            return new RemoteRecorderManagement.AuthenticationInfo()
            {
                UserKey = ConfigurationManager.AppSettings["PanoptoUsername"],
                Password = ConfigurationManager.AppSettings["PanoptoPassword"]
            };
        }

        /// <summary>
        /// Reads Panopto authentication information from app.settings (technically appsettings.config)
        /// </summary>
        private static AccessManagement.AuthenticationInfo AccessAuthentication()
        {
            return new AccessManagement.AuthenticationInfo()
            {
                UserKey = ConfigurationManager.AppSettings["PanoptoUsername"],
                Password = ConfigurationManager.AppSettings["PanoptoPassword"]
            };
        }

        /// <summary>
        /// Reads Panopto authentication information from app.settings (technically appsettings.config)
        /// </summary>
        private static SessionManagement.AuthenticationInfo SessionAuthentication()
        {
            return new SessionManagement.AuthenticationInfo()
            {
                UserKey = ConfigurationManager.AppSettings["PanoptoUsername"],
                Password = ConfigurationManager.AppSettings["PanoptoPassword"]
            };
        }

        /// <summary>
        /// Reads Panopto authentication information from app.settings (technically appsettings.config)
        /// </summary>
        private static UserManagement.AuthenticationInfo UserAuthentication()
        {
            return new UserManagement.AuthenticationInfo()
            {
                UserKey = ConfigurationManager.AppSettings["PanoptoUsername"],
                Password = ConfigurationManager.AppSettings["PanoptoPassword"]
            };
        }
        #endregion
    }
}
