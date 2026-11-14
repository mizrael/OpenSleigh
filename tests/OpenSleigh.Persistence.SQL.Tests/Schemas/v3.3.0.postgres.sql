DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'opensleigh') THEN
        CREATE SCHEMA opensleigh;
    END IF;
END $EF$;


CREATE TABLE opensleigh."OutboxMessages" (
    "MessageId" text NOT NULL,
    "CorrelationId" text NOT NULL,
    "Body" bytea NOT NULL,
    "MessageType" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ParentId" text,
    "SenderId" text NOT NULL,
    CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("MessageId")
);


CREATE TABLE opensleigh."SagaStates" (
    "InstanceId" text NOT NULL,
    "CorrelationId" text NOT NULL,
    "TriggerMessageId" text NOT NULL,
    "SagaType" text NOT NULL,
    "SagaStateType" text,
    "StateData" bytea,
    "IsCompleted" boolean NOT NULL,
    "LockId" text,
    "LockTime" timestamp with time zone,
    CONSTRAINT "PK_SagaStates" PRIMARY KEY ("InstanceId")
);


CREATE TABLE opensleigh."SagaProcessedMessages" (
    "InstanceId" text NOT NULL,
    "MessageId" text NOT NULL,
    "When" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SagaProcessedMessages" PRIMARY KEY ("InstanceId", "MessageId"),
    CONSTRAINT "FK_SagaProcessedMessages_SagaStates_InstanceId" FOREIGN KEY ("InstanceId") REFERENCES opensleigh."SagaStates" ("InstanceId") ON DELETE CASCADE
);


CREATE UNIQUE INDEX "IX_SagaStates_CorrelationId_SagaType" ON opensleigh."SagaStates" ("CorrelationId", "SagaType");


CREATE UNIQUE INDEX "IX_SagaStates_CorrelationId_SagaType_SagaStateType" ON opensleigh."SagaStates" ("CorrelationId", "SagaType", "SagaStateType");


