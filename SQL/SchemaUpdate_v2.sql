USE OfficeDDR
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Update Type for DAQ_LIR_A2';
GO
UPDATE TMadule
SET [Type] = 'analog input #2 [LIR]'
WHERE [MaduleName] = 'DAQ_LIR_A2';

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TUserProfile table';
GO
IF OBJECT_ID('TUserProfile', 'U') IS NOT NULL
    DROP TABLE TUserProfile;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TUserProfile_Ins';
GO

IF OBJECT_ID('TUserProfile_Ins', 'P') IS NOT NULL
    DROP PROCEDURE TUserProfile_Ins;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TUserProfile_GetLastRow';
GO

IF OBJECT_ID('TUserProfile_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TUserProfile_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TUserProfile_GetByUser';
GO

IF OBJECT_ID('TUserProfile_GetByUser', 'P') IS NOT NULL
    DROP PROCEDURE TUserProfile_GetByUser;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TUserProfile_DelByUser';
GO

IF OBJECT_ID('TUserProfile_DelByUser', 'P') IS NOT NULL
    DROP PROCEDURE TUserProfile_DelByUser;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop TDefaultProfile table';
GO
IF OBJECT_ID('TDefaultProfile', 'U') IS NOT NULL
    DROP TABLE TDefaultProfile;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDefaultProfile_DelByUser';
GO

IF OBJECT_ID('TDefaultProfile_DelByUser', 'P') IS NOT NULL
    DROP PROCEDURE TDefaultProfile_DelByUser;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDefaultProfile_GetAll';
GO

IF OBJECT_ID('TDefaultProfile_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE TDefaultProfile_GetAll;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDefaultProfile_GetLastRow';
GO

IF OBJECT_ID('TDefaultProfile_GetLastRow', 'P') IS NOT NULL
    DROP PROCEDURE TDefaultProfile_GetLastRow;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop stored procedure: TDefaultProfile_Ins';
GO

IF OBJECT_ID('TDefaultProfile_Ins', 'P') IS NOT NULL
    DROP PROCEDURE TDefaultProfile_Ins;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Schema Change: Add column UserId to TComments table';
GO
ALTER TABLE [dbo].[TComments]
ADD UserId INT;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Schema Change: Set UserId to 0 for all available rows in TComments table';
GO
UPDATE [dbo].[TComments]
SET UserId = 0;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Schema Change: Update TComments_Ins procedure';
GO
ALTER PROCEDURE [dbo].[TComments_Ins]
(
 @BaseID    bigint
,@Comment1	nvarchar(1000)
,@Comment2	nvarchar(1000)
,@Comment3	nvarchar(1000)
,@BitDepth	float
,@Depth		float
,@AreaName Nvarchar(50)
,@UserId   int
)
AS
BEGIN
	declare @CommentID bigint;
	set @CommentID = (select isnull(MAX(Commentid),0)+1 from [dbo].[TComments]);
	INSERT INTO [dbo].[TComments]
			   ([CommentID]
			   ,[BaseID]
			   ,[Comment1]
			   ,[Comment2]
			   ,[Comment3]
			   ,[BitDepth]
			   ,[Depth]
			   ,[LastUpdate]
			   ,[AreaName]
			   ,[UserId])
		 VALUES
			   (@CommentID
			   ,@BaseID
			   ,@Comment1
			   ,@Comment2
			   ,@Comment3
			   ,@BitDepth
			   ,@Depth
			   ,getdate()
			   ,@AreaName
			   ,@UserId);
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add TComments_GetByUserId procedure';
GO
CREATE PROCEDURE [dbo].[TComments_GetByUserId]
    @UserId INT
AS
BEGIN
    SELECT * 
    FROM TComments
    WHERE UserId = @UserId;
END;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Schema Change: Update TComments_GetByBaseId procedure';
GO
ALTER PROCEDURE [dbo].[TComments_GetByBaseId]
(
	 @MinBaseId Bigint
	,@MaxBaseId Bigint
	,@UserId int
)
AS
BEGIN
    SELECT * 
    FROM TComments
    WHERE BaseID BETWEEN @MinBaseId AND @MaxBaseId
          AND UserId = @UserId;
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TProcessData_GetByCode procedure';
GO
DROP PROCEDURE [dbo].[_TProcessData_GetByCode];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TProcessData_GetReange procedure';
GO
DROP PROCEDURE [dbo].[_TProcessData_GetReange];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TProcessData_GetLastRow procedure';
GO
DROP PROCEDURE [dbo].[_TProcessData_GetLastRow];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TComments_GetByCode procedure';
GO
DROP PROCEDURE [dbo].[_TComments_GetByCode];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TComments_GetReange procedure';
GO
DROP PROCEDURE [dbo].[_TComments_GetReange];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Drop _TComments_GetLastRow procedure';
GO
DROP PROCEDURE [dbo].[_TComments_GetLastRow];
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Rename _TProcessData_INS procedure to Rep_TProcessData_Insert';
GO
EXEC sp_rename 'dbo._TProcessData_INS', 'Rep_TProcessData_Insert';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Rename _TComments_INS procedure to Rep_TComments_Insert';
GO
EXEC sp_rename 'dbo._TComments_INS', 'Rep_TComments_Insert';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Rename _TComments_GetByID procedure to Rep_TComments_GetByCommentId';
GO
EXEC sp_rename 'dbo._TComments_GetByID', 'Rep_TComments_GetByCommentId';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Rename AB_Truncate_Table procedure to Rep_Local_AB_Truncate_Table';
GO
EXEC sp_rename 'dbo.AB_Truncate_Table', 'Rep_Local_AB_Truncate_Table';
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TProcessData_GetRange procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TProcessData_GetRange]
(
        @FirstCode bigint,
		@LastCode bigint
)
AS
BEGIN
	SET NOCOUNT ON;
	select * from TProcessData tt where tt.BaseID between @FirstCode and @LastCode;
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TProcessData_GetByBaseId procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TProcessData_GetByBaseId] 
    @BaseID BIGINT, 
    @MaxRows INT
AS
BEGIN
    IF (ISNULL(@BaseID, 0) = -1)
    BEGIN
        SELECT TOP (@MaxRows) * 
        FROM TProcessData
        WHERE BaseID = (SELECT MAX(BaseID) FROM TProcessData)
    END
    ELSE
    BEGIN
        SELECT TOP (@MaxRows) *
        FROM TProcessData
        WHERE BaseID > @BaseID
        ORDER BY BaseID
    END
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TProcessData_GetLastBaseId procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TProcessData_GetLastBaseId] 	
AS
BEGIN
	select * from TProcessData
	where BaseID = (select MAX(BaseID) from TProcessData)
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TComments_GetRange procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TComments_GetRange]
(
        @FirstCode bigint,
		@LastCode bigint
)
AS
BEGIN
	SET NOCOUNT ON;
	select * from TComments tt where tt.BaseID between @FirstCode and @LastCode;
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TComments_GetLastBaseId procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TComments_GetLastBaseId]
AS
BEGIN
	select * from TComments
	where BaseID = (select MAX(BaseID) from TComments)
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TComments_GetByBaseId procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TComments_GetByBaseId] 
    @BaseID BIGINT, 
    @MaxRows INT
AS
BEGIN
    IF (ISNULL(@BaseID, 0) = -1)
    BEGIN
        SELECT TOP (@MaxRows) * 
        FROM TComments
        WHERE BaseID = (SELECT MAX(BaseID) FROM TComments)
    END
    ELSE
    BEGIN
        SELECT TOP (@MaxRows) *
        FROM TComments
        WHERE BaseID > @BaseID
        ORDER BY BaseID
    END
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Rep_TProcesssData_GetBaseIdRanges procedure';
GO
CREATE PROCEDURE [dbo].[Rep_TProcesssData_GetBaseIdRanges]
AS
BEGIN
    SET NOCOUNT ON;
    WITH CTE AS (
        SELECT 
            baseid,
            baseid - ROW_NUMBER() OVER (ORDER BY baseid) AS grp
        FROM Tprocessdata
    )
    SELECT 
        MIN(baseid) AS baseidbegin,
        MAX(baseid) AS baseidend
    FROM CTE
    GROUP BY grp
    ORDER BY baseidbegin;
END
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: SPM Window Adjustment Method ';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    76,
    N'SPMWindowAdjustMethod',
    N'0: Manual 1: Adaptive',
    0,
    1,
    4,
    20,
    114,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: DisplacementPump1';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    77,
    N'DisplacementPump1',
    N'Pump1 Displacement (m3/stk)',
    0,
    1,
    4,
    5,
    29,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: DisplacementPump2';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    78,
    N'DisplacementPump2',
    N'Pump2 Displacement (m3/stk)',
    0,
    1,
    4,
    5,
    29,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: DisplacementPump3';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    79,
    N'DisplacementPump3',
    N'Pump3 Displacement (m3/stk)',
    0,
    1,
    4,
    5,
    29,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: StrokeLengthPump1';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    80,
    N'StrokeLengthPump1',
    N'Pump1 Stroke Length (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: StrokeLengthPump2';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    81,
    N'StrokeLengthPump2',
    N'Pump2 Stroke Length (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: StrokeLengthPump3';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    82,
    N'StrokeLengthPump3',
    N'Pump3 Stroke Length (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: LinearDiameterPump1';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    83,
    N'LinearDiameterPump1',
    N'Pump1 Linear Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: LinearDiameterPump2';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    84,
    N'LinearDiameterPump2',
    N'Pump2 Linear Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: LinearDiameterPump3';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    85,
    N'LinearDiameterPump3',
    N'Pump3 Linear Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: RodDiameterPump1';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    86,
    N'RodDiameterPump1',
    N'Pump1 Rod Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: RodDiameterPump2';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    87,
    N'RodDiameterPump2',
    N'Pump2 Rod Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Constant: RodDiameterPump3';
GO

INSERT INTO [dbo].[TConstant] (
    [ConstantID],
    [Name],
    [Descriptions],
    [UserValue],
    [IsReadOnly],
    [CategoryId],
    [DimensionId],
    [UnitId],
    [DefaultValue],
    [DateTimeValue],
    [LastUpdate]
)
VALUES (
    88,
    N'RodDiameterPump3',
    N'Pump3 Rod Diameter (in)',
    0,
    1,
    4,
    2,
    5,
    0,
    NULL,
    GETDATE()
)
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Update Constant: UnitId=101, DimensionId=13, UserValue=95';
GO

UPDATE [dbo].[TConstant]
SET 
    [DimensionId] = 13,
    [UnitId] = 101,
    [UserValue] = 95,
    [LastUpdate] = GETDATE()
WHERE [Name] IN (N'EfficiencyPump1', N'EfficiencyPump2', N'EfficiencyPump3');

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP5Cm';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP5Cm'                 -- ParamName
    , 10                        -- SortNumber
    , N'ROP5Cm'                 -- Caption
    , 100                       -- ColId
    , 25                        -- DimensionID
    , 119                       -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 5 cm'           -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP20Cm';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP20Cm'                -- ParamName
    , 10                        -- SortNumber
    , N'ROP20Cm'                -- Caption
    , 100                       -- ColId
    , 25                        -- DimensionID
    , 119                       -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 20 cm'          -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP100Cm';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP100Cm'               -- ParamName
    , 10                        -- SortNumber
    , N'ROP1M'                  -- Caption
    , 100                       -- ColId
    , 25                        -- DimensionID
    , 119                       -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 1 Meter'        -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP5Min';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP5Min'                -- ParamName
    , 10                        -- SortNumber
    , N'ROP5Min'                -- Caption
    , 100                       -- ColId
    , 2                         -- DimensionID
    , 8                         -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 5 min'          -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP20Min';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP20Min'               -- ParamName
    , 10                        -- SortNumber
    , N'ROP20Min'               -- Caption
    , 100                       -- ColId
    , 2                         -- DimensionID
    , 8                         -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 20 min'         -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: ROP60Min';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'ROP60Min'               -- ParamName
    , 10                        -- SortNumber
    , N'ROP1H'                  -- Caption
    , 100                       -- ColId
    , 2                         -- DimensionID
    , 8                         -- DefaultUnitID
    , 1                         -- CategoryID
    , N'ROP for 1 Hour'          -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 10                        -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Add Parameter: StripDepth';
GO

DECLARE @NextParameterID int =
(
    SELECT ISNULL(MAX(ParameterID), 0) + 1
    FROM dbo.TParameter
);

INSERT INTO dbo.TParameter (
      ParameterID
    , ParamName
    , SortNumber
    , Caption
    , ColId
    , DimensionID
    , DefaultUnitID
    , CategoryID
    , Description
    , IsPhysical
    , IsAnalog
    , IsDrillCommand
    , IsReadonly
    , IsStatus
    , IsActive
    , IsShowWellView
    , IsDrawChart
    , Offset
    , Coefficent
    , Tolerance
    , DecimalPoint
    , UpperBound
    , LowerBound
    , LastUpdate
    , Ohm
    , ScaleMin
    , ScaleMax
    , AlarmMin
    , AlarmMax
    , EnableAlarmMax
    , EnableAlarmMin
    , BackColor
    , ForeColor
)
VALUES (
      @NextParameterID          -- ParameterID
    , N'StripDepth'             -- ParamName
    , 10                        -- SortNumber
    , N'StripDepth'               -- Caption
    , 100                       -- ColId
    , 20                        -- DimensionID
    , 114                       -- DefaultUnitID
    , 1                         -- CategoryID
    , N'Strip Well Depth'       -- Description
    , 0                         -- IsPhysical
    , 0                         -- IsAnalog
    , 0                         -- IsDrillCommand
    , 1                         -- IsReadonly
    , 0                         -- IsStatus
    , 1                         -- IsActive
    , 1                         -- IsShowWellView
    , 1                         -- IsDrawChart
    , 0                         -- Offset
    , 1                         -- Coefficent
    , 0                         -- Tolerance
    , 2                         -- DecimalPoint
    , 0                         -- UpperBound
    , 0                         -- LowerBound
    , GETDATE()                 -- LastUpdate
    , 120                       -- Ohm
    , 0                         -- ScaleMin
    , 1                         -- ScaleMax
    , 0                         -- AlarmMin
    , 0                         -- AlarmMax
    , 0                         -- EnableAlarmMax
    , 0                         -- EnableAlarmMin
    , N'#FFFFFF'                -- BackColor
    , N'#000000'                -- ForeColor
);
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Alter Parameter: ROP2 caption';
GO
UPDATE dbo.TParameter
SET    Caption        = N'Avg ROP(m/h)',
       LastUpdate     = GETDATE()
WHERE  ParamName      = N'Rop2'
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Remove Parameter: RopMH20, RopMin100, RopMin25';
GO
DELETE FROM [dbo].[TParameter]
WHERE ParamName IN (N'RopMH20', N'RopMin100', N'RopMin25')
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Alter TProcessData: Add ROP5Cm, ROP20Cm, ROP100Cm, ROP5Min, ROP20Min, ROP60Min, StripDepth';
GO
ALTER TABLE [dbo].[TProcessData]
ADD
    [ROP5Cm]   [float] NULL,
    [ROP20Cm]  [float] NULL,
    [ROP100Cm] [float] NULL,
    [ROP5Min]  [float] NULL,
    [ROP20Min] [float] NULL,
    [ROP60Min] [float] NULL,
    [StripDepth] [float] NULL;
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Alter TProcessData: Remove RopMH20, RopMin100, RopMin25';
GO
ALTER TABLE [dbo].[TProcessData]
DROP COLUMN [RopMH20], [RopMin100], [RopMin25];
GO
----------------------------------------------------
----------------------------------------------------
PRINT 'Define Nonclustered Index Index_TProcessDataBaseIdDateTimeRecord for TProcessData...';
CREATE NONCLUSTERED INDEX [Index_TProcessDataBaseIdDateTimeRecord] ON [dbo].[TProcessData]
(
	[BaseID] ASC,
	[DateTimeRecord] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

----------------------------------------------------
----------------------------------------------------
PRINT 'Alter Strore Procedure: TProcessData_Ins - for ROP parameters';
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
