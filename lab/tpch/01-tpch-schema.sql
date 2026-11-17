/*
  S2-14 (DA-11) - Base de datos QD_TPCH (esquema estandar TPC-H, compatibilidad 160).
  Query Store habilitado en modo determinista de laboratorio (READ_WRITE, ALL, 1 min).
*/
USE master;
GO

IF DB_ID(N'QD_TPCH') IS NOT NULL
BEGIN
    ALTER DATABASE QD_TPCH SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE QD_TPCH;
END
GO

CREATE DATABASE QD_TPCH;
GO

ALTER DATABASE QD_TPCH SET COMPATIBILITY_LEVEL = 160;
ALTER DATABASE QD_TPCH SET RECOVERY SIMPLE;
ALTER DATABASE QD_TPCH SET QUERY_STORE = ON
(
    OPERATION_MODE = READ_WRITE,
    CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30),
    DATA_FLUSH_INTERVAL_SECONDS = 60,
    INTERVAL_LENGTH_MINUTES = 1,
    MAX_STORAGE_SIZE_MB = 1000,
    QUERY_CAPTURE_MODE = ALL,
    SIZE_BASED_CLEANUP_MODE = AUTO
);
GO

USE QD_TPCH;
GO

CREATE TABLE dbo.REGION
(
    R_REGIONKEY int          NOT NULL CONSTRAINT PK_REGION PRIMARY KEY CLUSTERED,
    R_NAME      char(25)     NOT NULL,
    R_COMMENT   varchar(152) NULL
);

CREATE TABLE dbo.NATION
(
    N_NATIONKEY int          NOT NULL CONSTRAINT PK_NATION PRIMARY KEY CLUSTERED,
    N_NAME      char(25)     NOT NULL,
    N_REGIONKEY int          NOT NULL,
    N_COMMENT   varchar(152) NULL
);

CREATE TABLE dbo.PART
(
    P_PARTKEY     int           NOT NULL CONSTRAINT PK_PART PRIMARY KEY CLUSTERED,
    P_NAME        varchar(55)   NOT NULL,
    P_MFGR        char(25)      NOT NULL,
    P_BRAND       char(10)      NOT NULL,
    P_TYPE        varchar(25)   NOT NULL,
    P_SIZE        int           NOT NULL,
    P_CONTAINER   char(10)      NOT NULL,
    P_RETAILPRICE decimal(12,2) NOT NULL,
    P_COMMENT     varchar(23)   NOT NULL
);

CREATE TABLE dbo.SUPPLIER
(
    S_SUPPKEY   int           NOT NULL CONSTRAINT PK_SUPPLIER PRIMARY KEY CLUSTERED,
    S_NAME      char(25)      NOT NULL,
    S_ADDRESS   varchar(40)   NOT NULL,
    S_NATIONKEY int           NOT NULL,
    S_PHONE     char(15)      NOT NULL,
    S_ACCTBAL   decimal(12,2) NOT NULL,
    S_COMMENT   varchar(101)  NOT NULL
);

CREATE TABLE dbo.PARTSUPP
(
    PS_PARTKEY    int           NOT NULL,
    PS_SUPPKEY    int           NOT NULL,
    PS_AVAILQTY   int           NOT NULL,
    PS_SUPPLYCOST decimal(12,2) NOT NULL,
    PS_COMMENT    varchar(199)  NOT NULL,
    CONSTRAINT PK_PARTSUPP PRIMARY KEY CLUSTERED (PS_PARTKEY, PS_SUPPKEY)
);

CREATE TABLE dbo.CUSTOMER
(
    C_CUSTKEY    int           NOT NULL CONSTRAINT PK_CUSTOMER PRIMARY KEY CLUSTERED,
    C_NAME       varchar(25)   NOT NULL,
    C_ADDRESS    varchar(40)   NOT NULL,
    C_NATIONKEY  int           NOT NULL,
    C_PHONE      char(15)      NOT NULL,
    C_ACCTBAL    decimal(12,2) NOT NULL,
    C_MKTSEGMENT char(10)      NOT NULL,
    C_COMMENT    varchar(117)  NOT NULL
);

CREATE TABLE dbo.ORDERS
(
    O_ORDERKEY      int           NOT NULL CONSTRAINT PK_ORDERS PRIMARY KEY CLUSTERED,
    O_CUSTKEY       int           NOT NULL,
    O_ORDERSTATUS   char(1)       NOT NULL,
    O_TOTALPRICE    decimal(12,2) NOT NULL,
    O_ORDERDATE     date          NOT NULL,
    O_ORDERPRIORITY char(15)      NOT NULL,
    O_CLERK         char(15)      NOT NULL,
    O_SHIPPRIORITY  int           NOT NULL,
    O_COMMENT       varchar(79)   NOT NULL
);

CREATE TABLE dbo.LINEITEM
(
    L_ORDERKEY      int           NOT NULL,
    L_PARTKEY       int           NOT NULL,
    L_SUPPKEY       int           NOT NULL,
    L_LINENUMBER    int           NOT NULL,
    L_QUANTITY      decimal(12,2) NOT NULL,
    L_EXTENDEDPRICE decimal(12,2) NOT NULL,
    L_DISCOUNT      decimal(12,2) NOT NULL,
    L_TAX           decimal(12,2) NOT NULL,
    L_RETURNFLAG    char(1)       NOT NULL,
    L_LINESTATUS    char(1)       NOT NULL,
    L_SHIPDATE      date          NOT NULL,
    L_COMMITDATE    date          NOT NULL,
    L_RECEIPTDATE   date          NOT NULL,
    L_SHIPINSTRUCT  char(25)      NOT NULL,
    L_SHIPMODE      char(10)      NOT NULL,
    L_COMMENT       varchar(44)   NOT NULL,
    CONSTRAINT PK_LINEITEM PRIMARY KEY CLUSTERED (L_ORDERKEY, L_LINENUMBER)
);
GO
