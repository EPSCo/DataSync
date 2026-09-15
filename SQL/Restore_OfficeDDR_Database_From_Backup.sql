USE master;

DECLARE @DatabaseName NVARCHAR(255) = 'OfficeDDR'; -- Set your new database name here
DECLARE @BackupFile NVARCHAR(255) = 'D:\DB\backup_20240709.bak'; -- Set the path to your backup file here

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = @DatabaseName)
BEGIN
    EXEC('CREATE DATABASE [' + @DatabaseName + '];');
END

DECLARE @BackupLogicalDataFileName NVARCHAR(128) = 'DDR';
DECLARE @BackupLogicalLogFileName NVARCHAR(128) = 'DDR_log';

DECLARE @DDRLogicalDataFilePath NVARCHAR(128) = 'D:\DB\OfficeDDR\DDR.mdf'; -- Adjust the paths as needed
DECLARE @DDRLogicalLogFilePath NVARCHAR(128) = 'D:\DB\OfficeDDR\DDR_log.ldf';  -- Adjust the paths as needed

RESTORE DATABASE @DatabaseName
FROM DISK = @BackupFile
WITH MOVE @BackupLogicalDataFileName TO @DDRLogicalDataFilePath,
     MOVE @BackupLogicalLogFileName TO @DDRLogicalLogFilePath,
     REPLACE; -- Use REPLACE if the database already exists
GO

USE OfficeDDR
GO
ALTER AUTHORIZATION ON DATABASE::OfficeDDR TO [sa]
GO