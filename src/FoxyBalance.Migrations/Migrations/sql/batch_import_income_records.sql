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
            ty.id,
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
        INNER JOIN foxybalance_taxyears ty ON ty.userid = p_user_id
            AND ty.taxyear = EXTRACT(YEAR FROM (rec->>'SaleDate')::TIMESTAMPTZ)::INT
        ON CONFLICT (sourcetransactionid, userid) WHERE sourcetransactionid IS NOT NULL
        DO UPDATE SET
            sourcetype = EXCLUDED.sourcetype,
            sourcetransactiondescription = EXCLUDED.sourcetransactiondescription,
            sourcetransactioncustomerdescription = EXCLUDED.sourcetransactioncustomerdescription,
            saledate = EXCLUDED.saledate,
            saleamount = EXCLUDED.saleamount,
            platformfee = EXCLUDED.platformfee,
            processingfee = EXCLUDED.processingfee,
            netshare = EXCLUDED.netshare
        RETURNING
            CASE
                WHEN xmax = 0 THEN 'INSERT'
                ELSE 'UPDATE'
            END as action,
            saleamount,
            platformfee,
            processingfee,
            netshare,
            taxyearid
    ),
    summary AS (
        SELECT
            COALESCE(COUNT(*) FILTER (WHERE m.action = 'INSERT'), 0) AS new_records,
            COALESCE(SUM(m.saleamount) FILTER (WHERE m.action = 'INSERT'), 0) AS total_sales,
            COALESCE(SUM(m.processingfee + m.platformfee) FILTER (WHERE m.action = 'INSERT'), 0) AS total_fees,
            COALESCE(SUM(m.netshare) FILTER (WHERE m.action = 'INSERT'), 0) AS total_net,
            COALESCE(SUM(m.netshare * ty.taxrate::NUMERIC / 100) FILTER (WHERE m.action = 'INSERT'), 0) AS total_tax
        FROM merged_records m
        INNER JOIN foxybalance_taxyears ty ON m.taxyearid = ty.id
    )
    SELECT
        COALESCE(new_records::INT, 0),
        COALESCE(total_sales::INT, 0),
        COALESCE(total_fees::INT, 0),
        COALESCE(total_net::INT, 0),
        COALESCE(total_tax::NUMERIC, 0)
    INTO
        v_new_records_count,
        v_total_sales,
        v_total_fees,
        v_total_net,
        v_total_tax
    FROM summary;

    -- Return the summary
    RETURN QUERY SELECT
        COALESCE(v_new_records_count, 0),
        COALESCE(v_total_sales, 0),
        COALESCE(v_total_fees, 0),
        COALESCE(v_total_net, 0),
        COALESCE(v_total_tax, 0::NUMERIC);
END;
$$ LANGUAGE plpgsql;
