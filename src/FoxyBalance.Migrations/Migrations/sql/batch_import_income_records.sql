CREATE OR REPLACE FUNCTION batch_import_income_records(
    p_user_id INT,
    p_records JSONB
)
RETURNS TABLE (
    total_new_records_imported INT,
    total_sales_imported INT,
    total_fees_imported INT,
    total_net_share_imported INT,
    total_estimated_taxes_imported NUMERIC
) AS $$
DECLARE
    v_default_tax_rate INT := 33;
    v_new_records_count INT;
    v_total_sales INT;
    v_total_fees INT;
    v_total_net INT;
    v_total_tax NUMERIC;
BEGIN
    -- Validation
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'p_user_id cannot be null';
    END IF;

    -- Create missing tax years from JSONB records
    INSERT INTO foxybalance_taxyears (userid, taxyear, taxrate)
    SELECT DISTINCT
        p_user_id,
        EXTRACT(YEAR FROM (rec->>'SaleDate')::TIMESTAMPTZ)::INT,
        v_default_tax_rate
    FROM jsonb_array_elements(p_records) AS rec
    WHERE NOT EXISTS (
        SELECT 1
        FROM foxybalance_taxyears t
        WHERE t.userid = p_user_id
          AND t.taxyear = EXTRACT(YEAR FROM (rec->>'SaleDate')::TIMESTAMPTZ)::INT
    );

    -- Use CTE to track inserted/updated records
    WITH merged_records AS (
        INSERT INTO foxybalance_incomerecords (
            userid, taxyearid, saledate, sourcetype,
            sourcetransactionid, sourcetransactiondescription,
            sourcetransactioncustomerdescription,
            saleamount, platformfee, processingfee, netshare, ignored
        )
        SELECT
            p_user_id,
            (SELECT id FROM foxybalance_taxyears
             WHERE userid = p_user_id
               AND taxyear = EXTRACT(YEAR FROM (rec->>'SaleDate')::TIMESTAMPTZ)::INT
             LIMIT 1),
            (rec->>'SaleDate')::TIMESTAMPTZ,
            rec->>'SourceType',
            rec->>'SourceTransactionId',
            rec->>'SourceTransactionDescription',
            rec->>'SourceTransactionCustomerDescription',
            (rec->>'SaleAmount')::INT,
            (rec->>'PlatformFee')::INT,
            (rec->>'ProcessingFee')::INT,
            (rec->>'NetShare')::INT,
            false
        FROM jsonb_array_elements(p_records) AS rec
        ON CONFLICT (sourcetransactionid, userid)
        WHERE sourcetransactionid IS NOT NULL
        DO UPDATE SET
            sourcetype = EXCLUDED.sourcetype,
            sourcetransactiondescription = EXCLUDED.sourcetransactiondescription,
            sourcetransactioncustomerdescription = EXCLUDED.sourcetransactioncustomerdescription,
            saledate = EXCLUDED.saledate,
            saleamount = EXCLUDED.saleamount,
            platformfee = EXCLUDED.platformfee,
            processingfee = EXCLUDED.processingfee,
            netshare = EXCLUDED.netshare
        RETURNING id,
            CASE
                WHEN xmax = 0 THEN 'INSERT'
                ELSE 'UPDATE'
            END as action
    ),
    summary AS (
        SELECT
            COUNT(*) FILTER (WHERE m.action = 'INSERT') AS new_records,
            COALESCE(SUM(v.saleamount) FILTER (WHERE m.action = 'INSERT'), 0) AS total_sales,
            COALESCE(SUM(v.processingfee + v.platformfee) FILTER (WHERE m.action = 'INSERT'), 0) AS total_fees,
            COALESCE(SUM(v.netshare) FILTER (WHERE m.action = 'INSERT'), 0) AS total_net,
            COALESCE(SUM(v.estimatedtax) FILTER (WHERE m.action = 'INSERT'), 0) AS total_tax
        FROM merged_records m
        INNER JOIN foxybalance_incomerecordsview v ON m.id = v.id
    )
    SELECT
        new_records::INT,
        total_sales::INT,
        total_fees::INT,
        total_net::INT,
        total_tax::NUMERIC
    INTO
        v_new_records_count,
        v_total_sales,
        v_total_fees,
        v_total_net,
        v_total_tax
    FROM summary;

    -- Return the summary
    RETURN QUERY SELECT
        v_new_records_count,
        v_total_sales,
        v_total_fees,
        v_total_net,
        v_total_tax;
END;
$$ LANGUAGE plpgsql;
