IF SCHEMA_ID(N'opensleigh') IS NULL EXEC(N'CREATE SCHEMA [opensleigh];');
GO


CREATE TABLE [opensleigh].[OutboxMessages] (
    [MessageId] nvarchar(450) NOT NULL,
    [CorrelationId] nvarchar(max) NOT NULL,
    [Body] varbinary(max) NOT NULL,
    [MessageType] nvarchar(max) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ParentId] nvarchar(max) NULL,
    [SenderId] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([MessageId])
);
GO


CREATE TABLE [opensleigh].[SagaStates] (
    [InstanceId] nvarchar(450) NOT NULL,
    [CorrelationId] nvarchar(450) NOT NULL,
    [TriggerMessageId] nvarchar(max) NOT NULL,
    [SagaType] nvarchar(450) NOT NULL,
    [SagaStateType] nvarchar(450) NULL,
    [StateData] varbinary(max) NULL,
    [IsCompleted] bit NOT NULL,
    [LockId] nvarchar(max) NULL,
    [LockTime] datetimeoffset NULL,
    CONSTRAINT [PK_SagaStates] PRIMARY KEY ([InstanceId])
);
GO


CREATE TABLE [opensleigh].[SagaProcessedMessages] (
    [InstanceId] nvarchar(450) NOT NULL,
    [MessageId] nvarchar(450) NOT NULL,
    [When] datetimeoffset NOT NULL,
    CONSTRAINT [PK_SagaProcessedMessages] PRIMARY KEY ([InstanceId], [MessageId]),
    CONSTRAINT [FK_SagaProcessedMessages_SagaStates_InstanceId] FOREIGN KEY ([InstanceId]) REFERENCES [opensleigh].[SagaStates] ([InstanceId]) ON DELETE CASCADE
);
GO


CREATE UNIQUE INDEX [IX_SagaStates_CorrelationId_SagaType] ON [opensleigh].[SagaStates] ([CorrelationId], [SagaType]);
GO


CREATE UNIQUE INDEX [IX_SagaStates_CorrelationId_SagaType_SagaStateType] ON [opensleigh].[SagaStates] ([CorrelationId], [SagaType], [SagaStateType]) WHERE [SagaStateType] IS NOT NULL;
GO


