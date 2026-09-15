USE [OfficeDDR];
GO

PRINT 'Starting delete from TRawData...';

SET NOCOUNT ON;

WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TRawData
    IF @@ROWCOUNT = 0 BREAK;
END
GO

PRINT 'Starting delete from TProcessData...';
SET NOCOUNT ON;
WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TProcessData;
    IF @@ROWCOUNT = 0 BREAK;
END
GO

PRINT 'Starting delete from TComments...';
SET NOCOUNT ON;
WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TComments;
    IF @@ROWCOUNT = 0 BREAK;
END

PRINT 'Starting delete from TCutting...';
SET NOCOUNT ON;
WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TCutting;
    IF @@ROWCOUNT = 0 BREAK;
END

PRINT 'Starting delete from TDepthLog...';
SET NOCOUNT ON;
WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TDepthLog;
    IF @@ROWCOUNT = 0 BREAK;
END

PRINT 'Starting delete from TUserCommand...';
SET NOCOUNT ON;
WHILE 1 = 1
BEGIN
    DELETE TOP (10000) FROM dbo.TUserCommand;
    IF @@ROWCOUNT = 0 BREAK;
END

PRINT 'Update  TSyncController to zero...';
UPDATE TSyncController SET CommandCount=0;