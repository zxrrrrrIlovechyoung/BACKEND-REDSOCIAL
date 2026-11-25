-- Agrega las columnas de auditoría requeridas por el modelo actual.
-- Es idempotente: puede ejecutarse más de una vez.
USE RedSocialDB;
GO

IF COL_LENGTH('dbo.Usuarios', 'FechaActualizacion') IS NULL
    ALTER TABLE dbo.Usuarios ADD FechaActualizacion DATETIME2 NULL;
IF COL_LENGTH('dbo.Usuarios', 'ModificadoPorUsuarioId') IS NULL
    ALTER TABLE dbo.Usuarios ADD ModificadoPorUsuarioId INT NULL;
IF COL_LENGTH('dbo.Usuarios', 'ModificadoPorTokenId') IS NULL
    ALTER TABLE dbo.Usuarios ADD ModificadoPorTokenId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.Momentos', 'ModificadoPorUsuarioId') IS NULL
    ALTER TABLE dbo.Momentos ADD ModificadoPorUsuarioId INT NULL;
IF COL_LENGTH('dbo.Momentos', 'ModificadoPorTokenId') IS NULL
    ALTER TABLE dbo.Momentos ADD ModificadoPorTokenId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.PerfilesUsuario', 'ModificadoPorUsuarioId') IS NULL
    ALTER TABLE dbo.PerfilesUsuario ADD ModificadoPorUsuarioId INT NULL;
IF COL_LENGTH('dbo.PerfilesUsuario', 'ModificadoPorTokenId') IS NULL
    ALTER TABLE dbo.PerfilesUsuario ADD ModificadoPorTokenId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.Reportes', 'FechaActualizacion') IS NULL
    ALTER TABLE dbo.Reportes ADD FechaActualizacion DATETIME2 NULL;
IF COL_LENGTH('dbo.Reportes', 'ModificadoPorUsuarioId') IS NULL
    ALTER TABLE dbo.Reportes ADD ModificadoPorUsuarioId INT NULL;
IF COL_LENGTH('dbo.Reportes', 'ModificadoPorTokenId') IS NULL
    ALTER TABLE dbo.Reportes ADD ModificadoPorTokenId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.ComentariosMomentos', 'FechaActualizacion') IS NULL
    ALTER TABLE dbo.ComentariosMomentos ADD FechaActualizacion DATETIME2 NULL;
IF COL_LENGTH('dbo.ComentariosMomentos', 'ModificadoPorUsuarioId') IS NULL
    ALTER TABLE dbo.ComentariosMomentos ADD ModificadoPorUsuarioId INT NULL;
IF COL_LENGTH('dbo.ComentariosMomentos', 'ModificadoPorTokenId') IS NULL
    ALTER TABLE dbo.ComentariosMomentos ADD ModificadoPorTokenId UNIQUEIDENTIFIER NULL;
GO
