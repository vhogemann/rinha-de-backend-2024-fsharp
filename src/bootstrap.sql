CREATE UNLOGGED TABLE balance (
    id SERIAL PRIMARY KEY,
    client_id INT NOT NULL,
    amount INT NOT NULL,
    overdraft_limit INT NOT NULL,
    CONSTRAINT amount_overdraft_limit_check CHECK (amount + overdraft_limit >= 0),
    CONSTRAINT balance_client_id_id_key UNIQUE (client_id, id)
);
CREATE INDEX balance_client_id_idx ON balance (client_id);

CREATE TYPE transaction_type AS ENUM ('DEPOSIT', 'WITHDRAWAL');

CREATE UNLOGGED TABLE transactions (
    id SERIAL PRIMARY KEY,
    client_id INT NOT NULL,
    amount INT NOT NULL,
    description TEXT,
    transaction_type transaction_type NOT NULL,
    transaction_date TIMESTAMP NOT NULL,
    CONSTRAINT transactions_client_id_id_key UNIQUE (client_id, id)
);
CREATE INDEX transactions_client_id_idx ON transactions (client_id);
CREATE INDEX transactions_transaction_date_idx ON transactions (transaction_date);

CREATE OR REPLACE PROCEDURE deposit(a_client_id INT, d_amount INT, d_description TEXT) AS $$
BEGIN
    UPDATE balance
    SET amount = amount + d_amount
    WHERE client_id = a_client_id;

    INSERT INTO transactions (client_id, amount, transaction_type, transaction_date, description)
    VALUES (a_client_id, d_amount, 'DEPOSIT', NOW(), d_description);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION withdrawal(a_client_id INT, w_amount INT, w_description TEXT) 
RETURNS TABLE (balance INT, overdraft INT, success BOOLEAN) AS $$
DECLARE
    current_balance INT;
    current_overdraft_limit INT;
BEGIN
    SELECT amount, overdraft_limit INTO current_balance, current_overdraft_limit FROM balance  WHERE client_id = a_client_id;
    IF (current_balance + current_overdraft_limit) >= w_amount THEN
        UPDATE balance
            SET amount = current_balance - w_amount
            WHERE client_id = a_client_id;
        INSERT INTO transactions (client_id, amount, transaction_type, transaction_date, description)
            VALUES (a_client_id, w_amount, 'WITHDRAWAL', NOW(), w_description);
        balance := current_balance - w_amount;
        overdraft := current_overdraft_limit;
        success := TRUE;
    ELSE
        balance := 0;
        overdraft := 0;
        success := FALSE;
    END IF;
    RETURN NEXT;
END;
$$ LANGUAGE plpgsql;

INSERT INTO balance (client_id, amount, overdraft_limit) VALUES 
    (1, 0, 100000),
    (2, 0, 80000),
    (3, 0, 1000000),
    (4, 0, 10000000),
    (5, 0, 500000),
    (99, 0, 100000);