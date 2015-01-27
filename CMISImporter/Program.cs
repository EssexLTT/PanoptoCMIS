using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

namespace CMISImporter
{
    class Program
    {
        static void Main(string[] args)
        {
        }


        private static void Process()
        {

        }

        #region CMIS
        private static IEnumerable<DataRow> GetTimetable(int iHoursAhead)
        {

        }
        #endregion

        #region Panopto
        private static RemoteRecorderManagement.RemoteRecorder GetRecorder(string sName)
        {
            //This method could be used to translate between rooms and remote recorder names.
            //To keep things simple in this example, we'll assume that the remote recorder name is the same as the room name (eg. remote recorder 'LTB06' is located in room 'LTB06')

        }

        private static 
        #endregion
    }
}
