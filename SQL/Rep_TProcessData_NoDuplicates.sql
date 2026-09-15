-- DDRREP: prevent duplicate TProcessData records in the LOCAL (replicated) database.
-- Run on the local database only (e.g. OfficeDDR), never on the remote DDR/DDM source database.
-- Safe to run more than once.

----------------------------------------------------
----------------------------------------------------
PRINT 'Remove duplicate TProcessData rows (keep one row per BaseID)';
GO
WITH Duplicates AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY BaseID ORDER BY (SELECT NULL)) AS RowNumber
    FROM [dbo].[TProcessData]
)
DELETE FROM Duplicates WHERE RowNumber > 1;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add unique index UX_TProcessData_BaseID';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TProcessData_BaseID' AND object_id = OBJECT_ID('dbo.TProcessData'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_TProcessData_BaseID] ON [dbo].[TProcessData] ([BaseID] ASC);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Alter stored procedure: TProcessData_Ins - skip records whose BaseID already exists';
GO

ALTER PROCEDURE [dbo].[TProcessData_Ins] 
 (
            @BaseID  bigint
           ,@DateTimeRecord  datetime
           ,@WOH  float
           ,@SPP  float
           ,@TorqueTD  float
           ,@RPMTD  float
           ,@Pit11  float
           ,@Pit12  float
           ,@AnnPr  float
           ,@FlowOut  float
           ,@TorqueRT  float
           ,@SA1  float
           ,@CMD_HBrake  float
           ,@CMD_Aclrt  float
           ,@CMD_Tt_RPM  float
           ,@CMD_Tt_Tq_Lmt  float
           ,@CMD_Tt_RT  float
           ,@CMD_Tt_ToTQ  float
           ,@Pit1  float
           ,@Pit2  float
           ,@Pit3  float
           ,@Pit4  float
           ,@Pit5  float
           ,@Pit6  float
           ,@Pit7  float
           ,@Pit8  float
           ,@Pit9  float
           ,@Pit10  float
           ,@TempIn  float
           ,@TempOut  float
           ,@MWInUp  float
           ,@MWInDown  float
           ,@MWOutUp  float
           ,@MWOutDown  float
           ,@SA2  float
           ,@SA3  float
           ,@SA4  float
           ,@SA5  float
           ,@SA6  float
           ,@SA7  float
           ,@SA8  float
           ,@SA9  float
           ,@CMD_Brake  float
           ,@SDI1  float
           ,@SDI2  float
           ,@SDI3  float
           ,@SDI4  float
           ,@SDI5  float
           ,@SDI6  float
           ,@SDI7  float
           ,@HookPos  float
           ,@RpmRT  float
           ,@SPuls1  float
           ,@SPM1  float
           ,@SPM2  float
           ,@SPM3  float
           ,@Spare  float
           ,@WellDepth  float
           ,@BitDepth  float
           ,@WOB  float
           ,@LagDepth  float
           ,@LagTime  float
           ,@Gain  float
           ,@Overpull  float
           ,@Drag  float
           ,@Loss  float
           ,@HookAccelerator  float
           ,@HookShift  float
           ,@HookVelocity  float
           ,@AnnularVolume  float
           ,@LineWork  float
           ,@FollowIn1  float
           ,@FollowIn2  float
           ,@FollowIn3  float
           ,@FollowIn  float
           ,@TotalStrock  float
           ,@TStork1  float
           ,@TStork2  float
           ,@TStork3  float
           ,@ActVol1  float
           ,@ActCap1  float
           ,@ActPercent1  float
           ,@ActVol2  float
           ,@ActCap2  float
           ,@ActPercent2  float
           ,@PtVol11  float
           ,@PtVol12  float
           ,@PtVol1  float
           ,@PtVol2  float
           ,@PtVol3  float
           ,@PtVol4  float
           ,@PtVol5  float
           ,@PtVol6  float
           ,@PtVol7  float
           ,@PtVol8  float
           ,@PtVol9  float
           ,@PtVol10  float
           ,@PTFLevel11  float
           ,@PTFLevel12  float
           ,@PTFLevel1  float
           ,@PTFLevel2  float
           ,@PTFLevel3  float
           ,@PTFLevel4  float
           ,@PTFLevel5  float
           ,@PTFLevel6  float
           ,@PTFLevel7  float
           ,@PTFLevel8  float
           ,@PTFLevel9  float
           ,@PTFLevel10  float
           ,@PitPecent11  float
           ,@PitPecent12  float
           ,@PitPecent1  float
           ,@PitPecent2  float
           ,@PitPecent3  float
           ,@PitPecent4  float
           ,@PitPecent5  float
           ,@PitPecent6  float
           ,@PitPecent7  float
           ,@PitPecent8  float
           ,@PitPecent9  float
           ,@PitPecent10  float
           ,@PTCap11  float
           ,@PTCap12  float
           ,@PTCap1  float
           ,@PTCap2  float
           ,@PTCap3  float
           ,@PTCap4  float
           ,@PTCap5  float
           ,@PTCap6  float
           ,@PTCap7  float
           ,@PTCap8  float
           ,@PTCap9  float
           ,@PTCap10  float
           ,@PTArea11  float
           ,@PTArea12  float
           ,@PTArea1  float
           ,@PTArea2  float
           ,@PTArea3  float
           ,@PTArea4  float
           ,@PTArea5  float
           ,@PTArea6  float
           ,@PTArea7  float
           ,@PTArea8  float
           ,@PTArea9  float
           ,@PTArea10  float
           ,@TMudP  float
           ,@MudP1  float
           ,@MudP2  float
           ,@MudP3  float
           ,@MWIn  float
           ,@MWOut  float
           ,@DiffTemp  float
           ,@RigStatus  float
           ,@BHAStatus  float
           ,@TDStatus  float
           ,@HookStatus  float
           ,@AutoStatus  float
           ,@ReamingMode  float
           ,@HRPump1  float
           ,@HRPump2  float
           ,@HRPump3  float
           ,@HRTD  float
           ,@HRRT  float
           ,@HRBit  float
           ,@Rop2  float
           ,@RPM  float
           ,@Torque  float
           ,@Tcirculation  float
           ,@SuToBottom  float
           ,@ActVol3  float
           ,@ActVol4  float
           ,@ActCap3  float
           ,@ActCap4  float
           ,@ActPercent3  float
           ,@ActPercent4  float
           ,@StringWeight  float
           ,@DiffFlow  float
           ,@DrillingCount  float
           ,@Wohoffslips  float
           ,@WohoffBottom  float
           ,@MPNS1  float
           ,@MPNS2  float
           ,@MPNS3  float
           ,@TotalMPNS  float
		   ,@TMPS1  float
           ,@TMPS2  float
           ,@TMPS3  float
           ,@TMPS  float
           ,@TotalSPM  float
           ,@TotalTripTnk  float
           ,@TotalPit  float
           ,@ROP5Cm  float
           ,@ROP20Cm  float
           ,@ROP100Cm  float
           ,@ROP5Min  float
           ,@ROP20Min  float
           ,@ROP60Min  float
           ,@StripDepth float
)

AS
BEGIN
	-- Skip records that were already copied (for example re-read after a failure).
	IF EXISTS (SELECT 1 FROM TProcessData WHERE BaseID = @BaseID)
		RETURN;

	insert into TProcessData
	(       [BaseID]
           ,[DateTimeRecord]
           ,[WOH]
           ,[SPP]
           ,[TorqueTD]
           ,[RPMTD]
           ,[Pit11]
           ,[Pit12]
           ,[AnnPr]
           ,[FlowOut]
           ,[TorqueRT]
           ,[SA1]
           ,[CMD_HBrake]
           ,[CMD_Aclrt]
           ,[CMD_Tt_RPM]
           ,[CMD_Tt_Tq_Lmt]
           ,[CMD_Tt_RT]
           ,[CMD_Tt_ToTQ]
           ,[Pit1]
           ,[Pit2]
           ,[Pit3]
           ,[Pit4]
           ,[Pit5]
           ,[Pit6]
           ,[Pit7]
           ,[Pit8]
           ,[Pit9]
           ,[Pit10]
           ,[TempIn]
           ,[TempOut]
           ,[MWInUp]
           ,[MWInDown]
           ,[MWOutUp]
           ,[MWOutDown]
           ,[SA2]
           ,[SA3]
           ,[SA4]
           ,[SA5]
           ,[SA6]
           ,[SA7]
           ,[SA8]
           ,[SA9]
           ,[CMD_Brake]
           ,[SDI1]
           ,[SDI2]
           ,[SDI3]
           ,[SDI4]
           ,[SDI5]
           ,[SDI6]
           ,[SDI7]
           ,[HookPos]
           ,[RpmRT]
           ,[SPuls1]
           ,[SPM1]
           ,[SPM2]
           ,[SPM3]
           ,[Spare]
           ,[WellDepth]
           ,[BitDepth]
           ,[WOB]
           ,[LagDepth]
           ,[LagTime]
           ,[Gain]
           ,[Overpull]
           ,[Drag]
           ,[Loss]
           ,[HookAccelerator]
           ,[HookShift]
           ,[HookVelocity]
           ,[AnnularVolume]
           ,[LineWork]
           ,[FollowIn1]
           ,[FollowIn2]
           ,[FollowIn3]
           ,[FollowIn]
           ,[TotalStrock]
           ,[TStork1]
           ,[TStork2]
           ,[TStork3]
           ,[ActVol1]
           ,[ActCap1]
           ,[ActPercent1]
           ,[ActVol2]
           ,[ActCap2]
           ,[ActPercent2]
           ,[PtVol11]
           ,[PtVol12]
           ,[PtVol1]
           ,[PtVol2]
           ,[PtVol3]
           ,[PtVol4]
           ,[PtVol5]
           ,[PtVol6]
           ,[PtVol7]
           ,[PtVol8]
           ,[PtVol9]
           ,[PtVol10]
           ,[PTFLevel11]
           ,[PTFLevel12]
           ,[PTFLevel1]
           ,[PTFLevel2]
           ,[PTFLevel3]
           ,[PTFLevel4]
           ,[PTFLevel5]
           ,[PTFLevel6]
           ,[PTFLevel7]
           ,[PTFLevel8]
           ,[PTFLevel9]
           ,[PTFLevel10]
           ,[PitPecent11]
           ,[PitPecent12]
           ,[PitPecent1]
           ,[PitPecent2]
           ,[PitPecent3]
           ,[PitPecent4]
           ,[PitPecent5]
           ,[PitPecent6]
           ,[PitPecent7]
           ,[PitPecent8]
           ,[PitPecent9]
           ,[PitPecent10]
           ,[PTCap11]
           ,[PTCap12]
           ,[PTCap1]
           ,[PTCap2]
           ,[PTCap3]
           ,[PTCap4]
           ,[PTCap5]
           ,[PTCap6]
           ,[PTCap7]
           ,[PTCap8]
           ,[PTCap9]
           ,[PTCap10]
           ,[PTArea11]
           ,[PTArea12]
           ,[PTArea1]
           ,[PTArea2]
           ,[PTArea3]
           ,[PTArea4]
           ,[PTArea5]
           ,[PTArea6]
           ,[PTArea7]
           ,[PTArea8]
           ,[PTArea9]
           ,[PTArea10]
           ,[TMudP]
           ,[MudP1]
           ,[MudP2]
           ,[MudP3]
           ,[MWIn]
           ,[MWOut]
           ,[DiffTemp]
           ,[RigStatus]
           ,[BHAStatus]
           ,[TDStatus]
           ,[HookStatus]
           ,[AutoStatus]
           ,[ReamingMode]
           ,[HRPump1]
           ,[HRPump2]
           ,[HRPump3]
           ,[HRTD]
           ,[HRRT]
           ,[HRBit]
           ,[Rop2]
           ,[RPM]
           ,[Torque]
           ,[Tcirculation]
           ,[SuToBottom]
           ,[ActVol3]
           ,[ActVol4]
           ,[ActCap3]
           ,[ActCap4]
           ,[ActPercent3]
           ,[ActPercent4]
           ,[StringWeight]
           ,[DiffFlow]
           ,[DrillingCount]
           ,[Wohoffslips]
           ,[WohoffBottom]
           ,[MPNS1]
           ,[MPNS2]
           ,[MPNS3]
           ,[TotalMPNS]
		   ,[TMPS1]  
           ,[TMPS2]  
           ,[TMPS3]  
           ,[TMPS]  
           ,[TotalSPM]  
           ,[TotalTripTnk]  
           ,[TotalPit]
           ,[ROP5Cm]
           ,[ROP20Cm]
           ,[ROP100Cm]
           ,[ROP5Min]
           ,[ROP20Min]
           ,[ROP60Min]
           ,[StripDepth]
	   
		   )
	Values
	(       @BaseID
           ,@DateTimeRecord
           ,@WOH
           ,@SPP
           ,@TorqueTD
           ,@RPMTD
           ,@Pit11
           ,@Pit12
           ,@AnnPr
           ,@FlowOut
           ,@TorqueRT
           ,@SA1
           ,@CMD_HBrake
           ,@CMD_Aclrt
           ,@CMD_Tt_RPM
           ,@CMD_Tt_Tq_Lmt
           ,@CMD_Tt_RT
           ,@CMD_Tt_ToTQ
           ,@Pit1
           ,@Pit2
           ,@Pit3
           ,@Pit4
           ,@Pit5
           ,@Pit6
           ,@Pit7
           ,@Pit8
           ,@Pit9
           ,@Pit10
           ,@TempIn
           ,@TempOut
           ,@MWInUp
           ,@MWInDown
           ,@MWOutUp
           ,@MWOutDown
           ,@SA2
           ,@SA3
           ,@SA4
           ,@SA5
           ,@SA6
           ,@SA7
           ,@SA8
           ,@SA9
           ,@CMD_Brake
           ,@SDI1
           ,@SDI2
           ,@SDI3
           ,@SDI4
           ,@SDI5
           ,@SDI6
           ,@SDI7
           ,@HookPos
           ,@RpmRT
           ,@SPuls1
           ,@SPM1
           ,@SPM2
           ,@SPM3
           ,@Spare
           ,@WellDepth
           ,@BitDepth
           ,@WOB
           ,@LagDepth
           ,@LagTime
           ,@Gain
           ,@Overpull
           ,@Drag
           ,@Loss
           ,@HookAccelerator
           ,@HookShift
           ,@HookVelocity
           ,@AnnularVolume
           ,@LineWork
           ,@FollowIn1
           ,@FollowIn2
           ,@FollowIn3
           ,@FollowIn
           ,@TotalStrock
           ,@TStork1
           ,@TStork2
           ,@TStork3
           ,@ActVol1
           ,@ActCap1
           ,@ActPercent1
           ,@ActVol2
           ,@ActCap2
           ,@ActPercent2
           ,@PtVol11
           ,@PtVol12
           ,@PtVol1
           ,@PtVol2
           ,@PtVol3
           ,@PtVol4
           ,@PtVol5
           ,@PtVol6
           ,@PtVol7
           ,@PtVol8
           ,@PtVol9
           ,@PtVol10
           ,@PTFLevel11
           ,@PTFLevel12
           ,@PTFLevel1
           ,@PTFLevel2
           ,@PTFLevel3
           ,@PTFLevel4
           ,@PTFLevel5
           ,@PTFLevel6
           ,@PTFLevel7
           ,@PTFLevel8
           ,@PTFLevel9
           ,@PTFLevel10
           ,@PitPecent11
           ,@PitPecent12
           ,@PitPecent1
           ,@PitPecent2
           ,@PitPecent3
           ,@PitPecent4
           ,@PitPecent5
           ,@PitPecent6
           ,@PitPecent7
           ,@PitPecent8
           ,@PitPecent9
           ,@PitPecent10
           ,@PTCap11
           ,@PTCap12
           ,@PTCap1
           ,@PTCap2
           ,@PTCap3
           ,@PTCap4
           ,@PTCap5
           ,@PTCap6
           ,@PTCap7
           ,@PTCap8
           ,@PTCap9
           ,@PTCap10
           ,@PTArea11
           ,@PTArea12
           ,@PTArea1
           ,@PTArea2
           ,@PTArea3
           ,@PTArea4
           ,@PTArea5
           ,@PTArea6
           ,@PTArea7
           ,@PTArea8
           ,@PTArea9
           ,@PTArea10
           ,@TMudP
           ,@MudP1
           ,@MudP2
           ,@MudP3
           ,@MWIn
           ,@MWOut
           ,@DiffTemp
           ,@RigStatus
           ,@BHAStatus
           ,@TDStatus
           ,@HookStatus
           ,@AutoStatus
           ,@ReamingMode
           ,@HRPump1
           ,@HRPump2
           ,@HRPump3
           ,@HRTD
           ,@HRRT
           ,@HRBit
           ,@Rop2
           ,@RPM
           ,@Torque
           ,@Tcirculation
           ,@SuToBottom
           ,@ActVol3
           ,@ActVol4
           ,@ActCap3
           ,@ActCap4
           ,@ActPercent3
           ,@ActPercent4
           ,@StringWeight
           ,@DiffFlow
           ,@DrillingCount
           ,@Wohoffslips
           ,@WohoffBottom
           ,@MPNS1
           ,@MPNS2
           ,@MPNS3
           ,@TotalMPNS
		   ,@TMPS1 
           ,@TMPS2
           ,@TMPS3 
           ,@TMPS
           ,@TotalSPM
           ,@TotalTripTnk 
           ,@TotalPit
           ,@ROP5Cm
           ,@ROP20Cm
           ,@ROP100Cm
           ,@ROP5Min
           ,@ROP20Min
           ,@ROP60Min
           ,@StripDepth

	)
END
GO
