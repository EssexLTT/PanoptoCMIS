USE [cmis]
GO
/****** Object:  UserDefinedFunction [dbo].[GetTTAllLecturers]    Script Date: 08/20/2014 16:21:58 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
/* **************************************************************************************** */
/*  UNIVERSITY OF ESSEX     SYSTEM:- Web function                                           */
/*  --------------------------------------------------------------------------------------  */
/*  PROGRAMME NAME : < GetTTAllLecturers > (CMIS)			                                */
/*     CREATE DATE : 09/02/2011                                                             */
/*          AUTHOR : B. Steeples                                                            */
/*        FUNCTION : Gets all Lecturers (by lecturerID) for a timetable slot (SlotID)		*/
/*					 Called by stored procs in WEBSQLLIVE.lecturerecording					*/
/*																							*/
/* **************************************************************************************** */
/*  HISTORY                                                                                 */
/*  1.00 New script                                                          BS 09/02/2011  */
/* **************************************************************************************** */

CREATE FUNCTION [dbo].[GetTTAllLecturers]
(
	@SlotId int,
	@TTperiod varchar(30)
)
RETURNS VARCHAR(200)
AS
BEGIN

--output
DECLARE @LecturerOutput Varchar(200)
SELECT @LecturerOutput = null

--check to see if multiple lecturers and slotentry nos. for slotid
IF EXISTS
(
	SELECT DISTINCT COALESCE(LecturerId,'Lecturer TBC') as LecturerId from TIMETABLE 
	WHERE TIMETABLE.SlotId = @SlotId
	AND SetId = @TTperiod
	AND SlotTotal > 1
	
)

BEGIN

------------------------------------
--GET ALL lecturers linked to that SlotID
------------------------------------
--multiple entries: run this
	SELECT 
		@LecturerOutput = COALESCE(@LecturerOutput + ', ', '') + RTRIM(TIMETABLE.LecturerId) 
					from TIMETABLE (NOLOCK)
					WHERE TIMETABLE.SlotId = @SlotId
					AND SetId = @TTperiod
					AND SlotTotal > 1
					ORDER BY LecturerId 
	
END	

ELSE

--single entry: run this
BEGIN
	SELECT  @LecturerOutput = COALESCE(LecturerId,'Lecturer TBC') 
			from TIMETABLE (NOLOCK)
			WHERE TIMETABLE.SlotId = @SlotId
			AND SetId = @TTperiod
			ORDER BY LecturerId desc
END
	
	-- Return the result of the function
	RETURN ISNULL(@LecturerOutput,'Lecturer TBC')

END


