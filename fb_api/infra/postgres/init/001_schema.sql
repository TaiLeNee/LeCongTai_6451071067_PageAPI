CREATE TABLE IF NOT EXISTS idempotency_keys (
    id SERIAL PRIMARY KEY,
    idempotency_key VARCHAR(255) UNIQUE NOT NULL,
    command_id VARCHAR(64) NOT NULL,
    action VARCHAR(50) NOT NULL,
    status VARCHAR(20) NOT NULL,
    response_data TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_idempotency_key ON idempotency_keys(idempotency_key);
CREATE INDEX IF NOT EXISTS idx_idempotency_status ON idempotency_keys(status);

CREATE TABLE IF NOT EXISTS command_status (
    id SERIAL PRIMARY KEY,
    command_id VARCHAR(64) UNIQUE NOT NULL,
    event_id VARCHAR(255),
    correlation_id VARCHAR(64),
    action VARCHAR(50) NOT NULL,
    status VARCHAR(30) NOT NULL,
    facebook_response TEXT,
    error_message TEXT,
    retry_count INT DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_command_id ON command_status(command_id);
CREATE INDEX IF NOT EXISTS idx_event_id ON command_status(event_id);
CREATE INDEX IF NOT EXISTS idx_command_status ON command_status(status);

CREATE TABLE IF NOT EXISTS comments (
    id SERIAL PRIMARY KEY,
    comment_id VARCHAR(255) UNIQUE NOT NULL,
    event_id VARCHAR(255),
    page_id VARCHAR(255),
    post_id VARCHAR(255),
    user_id VARCHAR(255),
    user_name VARCHAR(255),
    message TEXT,
    intent VARCHAR(50),
    sentiment VARCHAR(20),
    confidence DECIMAL(3,2),
    processed BOOLEAN DEFAULT FALSE,
    action_taken VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_comment_id ON comments(comment_id);
CREATE INDEX IF NOT EXISTS idx_page_id ON comments(page_id);

ALTER TABLE idempotency_keys
    ALTER COLUMN command_id TYPE VARCHAR(64) USING command_id::text;

ALTER TABLE command_status
    ALTER COLUMN command_id TYPE VARCHAR(64) USING command_id::text,
    ALTER COLUMN event_id TYPE VARCHAR(255) USING event_id::text,
    ALTER COLUMN correlation_id TYPE VARCHAR(64) USING correlation_id::text;
