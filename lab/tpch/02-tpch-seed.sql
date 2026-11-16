/*
  S2-14 (DA-11) - Carga sintetica determinista TPC-H (SF = 0.1, sin PII).
  Tamanos: REGION=5, NATION=25, SUPPLIER=1.000, PART=20.000, PARTSUPP=80.000,
           CUSTOMER=15.000, ORDERS=150.000, LINEITEM=600.000 (4 lineas por orden).
*/
USE QD_TPCH;
GO
SET NOCOUNT ON;

INSERT dbo.REGION (R_REGIONKEY, R_NAME, R_COMMENT) VALUES
(0, 'AFRICA', 'synthetic region 0'),
(1, 'AMERICA', 'synthetic region 1'),
(2, 'ASIA', 'synthetic region 2'),
(3, 'EUROPE', 'synthetic region 3'),
(4, 'MIDDLE EAST', 'synthetic region 4');

INSERT dbo.NATION (N_NATIONKEY, N_NAME, N_REGIONKEY, N_COMMENT) VALUES
(0, 'ALGERIA', 0, 'n0'), (1, 'ARGENTINA', 1, 'n1'), (2, 'BRAZIL', 1, 'n2'),
(3, 'CANADA', 1, 'n3'), (4, 'EGYPT', 4, 'n4'), (5, 'ETHIOPIA', 0, 'n5'),
(6, 'FRANCE', 3, 'n6'), (7, 'GERMANY', 3, 'n7'), (8, 'INDIA', 2, 'n8'),
(9, 'INDONESIA', 2, 'n9'), (10, 'IRAN', 4, 'n10'), (11, 'IRAQ', 4, 'n11'),
(12, 'JAPAN', 2, 'n12'), (13, 'JORDAN', 4, 'n13'), (14, 'KENYA', 0, 'n14'),
(15, 'MOROCCO', 0, 'n15'), (16, 'MOZAMBIQUE', 0, 'n16'), (17, 'PERU', 1, 'n17'),
(18, 'CHINA', 2, 'n18'), (19, 'ROMANIA', 3, 'n19'), (20, 'SAUDI ARABIA', 4, 'n20'),
(21, 'VIETNAM', 2, 'n21'), (22, 'RUSSIA', 3, 'n22'), (23, 'UNITED KINGDOM', 3, 'n23'),
(24, 'UNITED STATES', 1, 'n24');

;WITH n AS (
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.SUPPLIER (S_SUPPKEY, S_NAME, S_ADDRESS, S_NATIONKEY, S_PHONE, S_ACCTBAL, S_COMMENT)
SELECT
    i,
    CONCAT('Supplier#', RIGHT('000000000' + CAST(i AS varchar(9)), 9)),
    CONCAT('Address ', i),
    (i * 7) % 25,
    CONCAT(10 + ((i * 7) % 25), '-', 100 + (i % 900), '-', 100 + ((i * 3) % 900), '-', 1000 + (i % 9000)),
    CAST(((i * 193) % 1100000 - 100000) / 100.0 AS decimal(12,2)),
    CASE WHEN i % 50 = 0 THEN 'Customer Complaints synthetic note' ELSE 'regular supplier comment' END
FROM n;

;WITH n AS (
    SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.PART (P_PARTKEY, P_NAME, P_MFGR, P_BRAND, P_TYPE, P_SIZE, P_CONTAINER, P_RETAILPRICE, P_COMMENT)
SELECT
    i,
    CONCAT('part ', CASE i % 5 WHEN 0 THEN 'green ' WHEN 1 THEN 'steel ' WHEN 2 THEN 'copper ' WHEN 3 THEN 'brass ' ELSE 'nickel ' END, i),
    CONCAT('Manufacturer#', 1 + (i % 5)),
    CONCAT('Brand#', 1 + (i % 5), 1 + ((i / 5) % 5)),
    CONCAT(CASE i % 3 WHEN 0 THEN 'PROMO ' WHEN 1 THEN 'STANDARD ' ELSE 'ECONOMY ' END,
           CASE i % 4 WHEN 0 THEN 'BRUSHED COPPER' WHEN 1 THEN 'ANODIZED BRASS' WHEN 2 THEN 'BURNISHED STEEL' ELSE 'PLATED TIN' END),
    1 + (i % 50),
    CASE i % 6 WHEN 0 THEN 'SM CASE' WHEN 1 THEN 'SM BOX' WHEN 2 THEN 'MED BAG' WHEN 3 THEN 'MED PKG' WHEN 4 THEN 'LG CASE' ELSE 'JUMBO DRUM' END,
    CAST(900.0 + ((i * 17) % 110000) / 100.0 AS decimal(12,2)),
    CONCAT('pc_', i % 100)
FROM n;

;WITH n AS (
    SELECT TOP (80000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.PARTSUPP (PS_PARTKEY, PS_SUPPKEY, PS_AVAILQTY, PS_SUPPLYCOST, PS_COMMENT)
SELECT
    ((i - 1) / 4) + 1,
    1 + ((((i - 1) / 4) + ((i - 1) % 4) * 250) % 1000),
    1 + ((i * 29) % 9999),
    CAST(1.0 + ((i * 43) % 100000) / 100.0 AS decimal(12,2)),
    CONCAT('ps_comment_', i % 200)
FROM n;

;WITH n AS (
    SELECT TOP (15000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.CUSTOMER (C_CUSTKEY, C_NAME, C_ADDRESS, C_NATIONKEY, C_PHONE, C_ACCTBAL, C_MKTSEGMENT, C_COMMENT)
SELECT
    i,
    CONCAT('Customer#', RIGHT('000000000' + CAST(i AS varchar(9)), 9)),
    CONCAT('CustAddr ', i),
    (i * 11) % 25,
    CONCAT(10 + ((i * 11) % 25), '-', 100 + (i % 900), '-', 100 + ((i * 5) % 900), '-', 1000 + (i % 9000)),
    CAST(((i * 137) % 1000000 - 100000) / 100.0 AS decimal(12,2)),
    CASE i % 5 WHEN 0 THEN 'AUTOMOBILE' WHEN 1 THEN 'BUILDING' WHEN 2 THEN 'FURNITURE' WHEN 3 THEN 'MACHINERY' ELSE 'HOUSEHOLD' END,
    CASE WHEN i % 25 = 0 THEN 'unusual package requests' ELSE 'regular customer' END
FROM n;

;WITH n AS (
    SELECT TOP (150000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c
)
INSERT dbo.ORDERS (O_ORDERKEY, O_CUSTKEY, O_ORDERSTATUS, O_TOTALPRICE, O_ORDERDATE, O_ORDERPRIORITY, O_CLERK, O_SHIPPRIORITY, O_COMMENT)
SELECT
    i,
    1 + ((i * 13) % 15000),
    CASE i % 3 WHEN 0 THEN 'F' WHEN 1 THEN 'O' ELSE 'P' END,
    CAST(1000.0 + ((i * 211) % 35000000) / 100.0 AS decimal(12,2)),
    DATEADD(DAY, i % 2406, '1992-01-01'),
    CASE i % 5 WHEN 0 THEN '1-URGENT' WHEN 1 THEN '2-HIGH' WHEN 2 THEN '3-MEDIUM' WHEN 3 THEN '4-NOT SPECIFIED' ELSE '5-LOW' END,
    CONCAT('Clerk#', RIGHT('000000000' + CAST(1 + (i % 100) AS varchar(9)), 9)),
    0,
    CASE WHEN i % 30 = 0 THEN 'special requests express' ELSE CONCAT('order_note_', i % 50) END
FROM n;

;WITH n AS (
    SELECT TOP (600000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c
),
base AS (
    SELECT
        ((i - 1) / 4) + 1 AS order_key,
        1 + ((i - 1) % 4) AS line_num,
        1 + ((i * 17) % 20000) AS part_key,
        1 + ((i * 31) % 1000) AS supp_key,
        CAST(1 + (i % 50) AS decimal(12,2)) AS qty,
        DATEADD(DAY, ((i - 1) / 4) % 2406, '1992-01-01') AS order_date,
        i
    FROM n
)
INSERT dbo.LINEITEM (
    L_ORDERKEY, L_PARTKEY, L_SUPPKEY, L_LINENUMBER, L_QUANTITY,
    L_EXTENDEDPRICE, L_DISCOUNT, L_TAX, L_RETURNFLAG, L_LINESTATUS,
    L_SHIPDATE, L_COMMITDATE, L_RECEIPTDATE, L_SHIPINSTRUCT, L_SHIPMODE, L_COMMENT)
SELECT
    b.order_key,
    b.part_key,
    b.supp_key,
    b.line_num,
    b.qty,
    CAST(b.qty * (900.0 + ((b.part_key * 17) % 1100)) AS decimal(12,2)),
    CAST((b.i % 11) / 100.0 AS decimal(12,2)),
    CAST((b.i % 9) / 100.0 AS decimal(12,2)),
    CASE WHEN DATEADD(DAY, 1 + (b.i % 120), b.order_date) <= '1995-06-17' THEN (CASE b.i % 2 WHEN 0 THEN 'R' ELSE 'A' END) ELSE 'N' END,
    CASE WHEN DATEADD(DAY, 1 + (b.i % 120), b.order_date) <= '1995-06-17' THEN 'F' ELSE 'O' END,
    DATEADD(DAY, 1 + (b.i % 120), b.order_date),
    DATEADD(DAY, 30 + (b.i % 60), b.order_date),
    DATEADD(DAY, 2 + (b.i % 125), b.order_date),
    CASE b.i % 4 WHEN 0 THEN 'DELIVER IN PERSON' WHEN 1 THEN 'COLLECT COD' WHEN 2 THEN 'NONE' ELSE 'TAKE BACK RETURN' END,
    CASE b.i % 7 WHEN 0 THEN 'AIR' WHEN 1 THEN 'TRUCK' WHEN 2 THEN 'MAIL' WHEN 3 THEN 'SHIP' WHEN 4 THEN 'RAIL' WHEN 5 THEN 'REG AIR' ELSE 'FOB' END,
    CONCAT('li_', b.i % 100)
FROM base b;
GO

SELECT
    (SELECT COUNT(*) FROM dbo.REGION) AS region_rows,
    (SELECT COUNT(*) FROM dbo.NATION) AS nation_rows,
    (SELECT COUNT(*) FROM dbo.SUPPLIER) AS supplier_rows,
    (SELECT COUNT(*) FROM dbo.PART) AS part_rows,
    (SELECT COUNT(*) FROM dbo.PARTSUPP) AS partsupp_rows,
    (SELECT COUNT(*) FROM dbo.CUSTOMER) AS customer_rows,
    (SELECT COUNT(*) FROM dbo.ORDERS) AS orders_rows,
    (SELECT COUNT(*) FROM dbo.LINEITEM) AS lineitem_rows;
GO
