USE [cmis]
GO
/****** Object:  UserDefinedFunction [dbo].[GetTTAllModules]    Script Date: 08/20/2014 16:21:55 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
/* **************************************************************************************** */
/*  UNIVERSITY OF ESSEX     SYSTEM:- Web function                                           */
/*  --------------------------------------------------------------------------------------  */
/*  PROGRAMME NAME : < GetTTAllModules > (CMIS)			                                    */
/*     CREATE DATE : 09/02/2011                                                             */
/*          AUTHOR : B. Steeples                                                            */
/*        FUNCTION : Gets all Modules (by moduleID) for a timetable slot (SlotID)			*/
/*					 Called by stored procs in WEBSQLLIVE.lecturerecording					*/
/*																							*/
/* **************************************************************************************** */
/*  HISTORY                                                                                 */
/*  1.00 New script                                                          BS 09/02/2011  */
/* **************************************************************************************** */

CREATE FUNCTION [dbo].[GetTTAllModules]
(
	@SlotId int,
	@TTperiod varchar(30)
)
RETURNS VARCHAR(200)
AS
BEGIN

--output
DECLARE @ModuleOutput Varchar(200)
SELECT @ModuleOutput = null

--check to see if multiple Modules and slotentry nos. for slotid
IF EXISTS
(
	SELECT DISTINCT COALESCE(ModuleId,'Module TBC') as ModuleId from TIMETABLE 
	WHERE TIMETABLE.SlotId = @SlotId
	AND SetId = @TTperiod
	AND SlotTotal > 1
)

BEGIN

------------------------------------
--GET ALL Modules linked to that SlotID
------------------------------------
--multiple entries: run this
	SELECT 
		@ModuleOutput = COALESCE(@ModuleOutput + ', ', '') + RTRIM(TIMETABLE.ModuleId) 
					from TIMETABLE (NOLOCK)
					WHERE TIMETABLE.SlotId = @SlotId
					AND SetId = @TTperiod
					AND SlotTotal > 1
					ORDER BY ModuleId 
	
END	

ELSE

--single entry: run this
BEGIN
	SELECT  @ModuleOutput = COALESCE(ModuleId,'Module TBC') 
			from TIMETABLE (NOLOCK)
			WHERE TIMETABLE.SlotId = @SlotId
			AND SetId = @TTperiod
			ORDER BY ModuleId desc
END
	
	-- Return the result of the function
	RETURN ISNULL(@ModuleOutput,'Module TBC')

END


