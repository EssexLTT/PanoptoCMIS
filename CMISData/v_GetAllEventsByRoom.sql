SELECT     SetId, SlotId, SlotEntry, StartTime, Duration, FinishTime, RoomId, ClassId, ClassificationName, ModGrpCode, ModuleIds, DeptId, LecturerIds
FROM         (SELECT     TT.SetId, TT.SlotId, TT.SlotEntry, TT.SlotTotal, TT.InstId, CONVERT(DATETIME, COALESCE (CCM.MapDate, '19000101') + COALESCE (TT.StartTime, '00:00')) 
                                              AS StartTime, TT.Duration, DATEADD(MINUTE, TT.Duration, CONVERT(DATETIME, COALESCE (CCM.MapDate, '19000101') + COALESCE (TT.StartTime, 
                                              '00:00'))) AS FinishTime, CCM.MapDate, TT.[weekday], TT.RoomId, CL.ClassId, CL.Name AS ClassificationName, TT.ModGrpCode, 
                                              dbo.GetTTAllModules(TT.SlotId, TT.SetId) AS ModuleIds, TT.DeptId, dbo.GetTTAllLecturers(TT.SlotId, TT.SetId) AS LecturerIds, CH.ChangedOn, 
                                              CH.ChangeKey, CH.ChangeDataType, RANK() OVER (PARTITION BY TT.ModuleId, TT.LecturerId, TT.StartTime, TT.FinishTime, TT.Duration, TT.[weekday], 
                                              TT.WeekId, TT.SetId, TT.SlotId, TT.SlotEntry, TT.SlotTotal, CCM.MapDate, TT.RoomId, CH.ChangeDataType, TT.ModGrpCode, TT.ClsGrpCode
                       ORDER BY CH.ChangeKey DESC, CH.ChangedOn DESC) AS RNK
FROM         dbo.TIMETABLE AS TT WITH (NOLOCK) INNER JOIN
                      dbo.CCALMAPS AS CCM WITH (NOLOCK) ON TT.WeekDay = CCM.DayPosn AND TT.SetId = CCM.SetId INNER JOIN
                      dbo.WEEKMAPNUMERIC AS WMN WITH (NOLOCK) ON CCM.SetId = WMN.SetId AND TT.WeekId = WMN.WeekId AND CCM.WeekNum = WMN.WeekNumber INNER JOIN
                      dbo.CLASSIFICATIONS AS CL WITH (NOLOCK) ON TT.SetId = CL.SetId AND TT.Classif = CL.ClassId LEFT OUTER JOIN
                      dbo.CHANGES AS CH WITH (NOLOCK) ON TT.SetId = CH.SetId AND TT.SlotId = CH.NumData1
WHERE     (CL.Type = 'TT_SLOT') AND SlotEntry = 1
GROUP BY TT.SetId, TT.SlotId, TT.SlotEntry, TT.SlotTotal, TT.InstId, TT.StartTime, TT.Duration, TT.FinishTime, CCM.MapDate, TT.[weekday], TT.weekid, TT.RoomId, CL.ClassId, 
                      TT.ModuleId, TT.LecturerId, CH.ChangedOn, CH.ChangeDataType, CH.ChangeKey, CL.Name, TT.ModGrpCode, TT.ClsGrpCode, TT.DeptId) AS DTA
WHERE     RNK = 1
