USE OfficeDDR
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop Unused Tables By DDM';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TRawData table';
GO
IF OBJECT_ID('TRawData', 'U') IS NOT NULL
    DROP TABLE TRawData;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TWellInfo table';
GO
IF OBJECT_ID('TWellInfo', 'U') IS NOT NULL
    DROP TABLE TWellInfo;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TSyncController table';
GO
IF OBJECT_ID('TSyncController', 'U') IS NOT NULL
    DROP TABLE TSyncController;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TRollsInfo table';
GO
IF OBJECT_ID('TRollsInfo', 'U') IS NOT NULL
    DROP TABLE TRollsInfo;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TPoint table';
GO
IF OBJECT_ID('TPoint', 'U') IS NOT NULL
    DROP TABLE TPoint;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TMadule table';
GO
IF OBJECT_ID('TMadule', 'U') IS NOT NULL
    DROP TABLE TMadule;
GO
----------------------------------------------------
----------------------------------------------------
PRINT 'Drop THoleDefinition table';
GO
IF OBJECT_ID('THoleDefinition', 'U') IS NOT NULL
    DROP TABLE THoleDefinition;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TDepthLog table';
GO
IF OBJECT_ID('TDepthLog', 'U') IS NOT NULL
    DROP TABLE TDepthLog;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TCutting table';
GO
IF OBJECT_ID('TCutting', 'U') IS NOT NULL
    DROP TABLE TCutting;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TChannelData table';
GO
IF OBJECT_ID('TChannelData', 'U') IS NOT NULL
    DROP TABLE TChannelData;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop Store Procedures Related to Unused Tables By DDM';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TWellInfo_GetAll';
GO

IF OBJECT_ID('TWellInfo_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TWellInfo_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TSyncController_GetLastRow';
GO

IF OBJECT_ID('TSyncController_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TSyncController_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TSync_GetAll';
GO

IF OBJECT_ID('TSync_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TSync_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TRawData_Ins';
GO

IF OBJECT_ID('TRawData_Ins', 'P') IS NOT NULL
    DROP PROCEDURE TRawData_Ins;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TRawData_GetRawbyBaseID';
GO

IF OBJECT_ID('TRawData_GetRawbyBaseID', 'P') IS NOT NULL
    DROP PROCEDURE TRawData_GetRawbyBaseID;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TRawData_GetLastRow';
GO

IF OBJECT_ID('TRawData_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TRawData_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TRawData_GetAll';
GO

IF OBJECT_ID('TRawData_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TRawData_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: Tpoint_Insert';
GO

IF OBJECT_ID('Tpoint_Insert', 'P') IS NOT NULL
    DROP PROCEDURE Tpoint_Insert;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: Tpoint_GetLastRow';
GO

IF OBJECT_ID('Tpoint_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE Tpoint_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: Tpoint_GetAll';
GO

IF OBJECT_ID('Tpoint_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE Tpoint_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: Tpoint_Delete_ByName';
GO

IF OBJECT_ID('Tpoint_Delete_ByName', 'P') IS NOT NULL
    DROP PROCEDURE Tpoint_Delete_ByName;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TMadule_UPD_DYN';
GO

IF OBJECT_ID('TMadule_UPD_DYN', 'P') IS NOT NULL
    DROP PROCEDURE TMadule_UPD_DYN;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TMadule_GetAll';
GO

IF OBJECT_ID('TMadule_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TMadule_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDepthLog_Update_DeleteFlag';
GO

IF OBJECT_ID('TDepthLog_Update_DeleteFlag', 'P') IS NOT NULL
    DROP PROCEDURE TDepthLog_Update_DeleteFlag;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDepthLog_Ins';
GO

IF OBJECT_ID('TDepthLog_Ins', 'P') IS NOT NULL
    DROP PROCEDURE TDepthLog_Ins;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDepthLog_GetLastRow';
GO

IF OBJECT_ID('TDepthLog_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TDepthLog_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDepthLog_GetAll';
GO

IF OBJECT_ID('TDepthLog_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TDepthLog_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDepthLog_Get_Top200_Valid_Row';
GO

IF OBJECT_ID('TDepthLog_Get_Top200_Valid_Row', 'P') IS NOT NULL
    DROP PROCEDURE TDepthLog_Get_Top200_Valid_Row;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TCutting_Ins';
GO

IF OBJECT_ID('TCutting_Ins', 'P') IS NOT NULL
    DROP PROCEDURE TCutting_Ins;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TCutting_GetAll';
GO

IF OBJECT_ID('TCutting_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TCutting_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TCutting_DeleteByListID';
GO

IF OBJECT_ID('TCutting_DeleteByListID', 'P') IS NOT NULL
    DROP PROCEDURE TCutting_DeleteByListID;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_GetLastRow';
GO

IF OBJECT_ID('TChannelData_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_GetAll';
GO

IF OBJECT_ID('TChannelData_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TChannelData_Upd';
GO

IF OBJECT_ID('TChannelData_Upd', 'P') IS NOT NULL
    DROP PROCEDURE TChannelData_Upd;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: Rep_Local_AB_Truncate_Table';
GO

IF OBJECT_ID('Rep_Local_AB_Truncate_Table', 'P') IS NOT NULL
    DROP PROCEDURE Rep_Local_AB_Truncate_Table;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add stored procedure: Rep_Local_Truncate_Table';
GO

CREATE PROCEDURE [dbo].[Rep_Local_Truncate_Table]
AS
BEGIN
	truncate table TProcessData
	truncate table TComments
END
