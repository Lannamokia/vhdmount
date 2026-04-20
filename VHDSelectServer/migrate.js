#!/usr/bin/env node

require('dotenv').config();

const { createDatabase } = require('./database');
const { SecurityStore } = require('./securityStore');
const { getLatestSchemaVersion } = require('./schemaMigrations');

function parseBoolean(value) {
    return ['1', 'true', 'yes', 'on'].includes(String(value || '').trim().toLowerCase());
}

function getEnvDbConfig() {
    return {
        host: process.env.DB_HOST,
        port: process.env.DB_PORT,
        database: process.env.DB_NAME,
        user: process.env.DB_USER,
        password: process.env.DB_PASSWORD,
        ssl: parseBoolean(process.env.DB_SSL),
        max: process.env.DB_MAX_CONNECTIONS,
        idleTimeoutMillis: process.env.DB_IDLE_TIMEOUT,
        connectionTimeoutMillis: process.env.DB_CONNECTION_TIMEOUT,
    };
}

function hasCompleteEnvDbConfig(config) {
    return ['host', 'database', 'user', 'password'].every((key) => String(config[key] || '').trim().length > 0);
}

function resolveDbConfig() {
    const envConfig = getEnvDbConfig();
    if (hasCompleteEnvDbConfig(envConfig)) {
        return envConfig;
    }

    const configDir = process.env.CONFIG_PATH || __dirname;
    const securityStore = new SecurityStore(configDir);
    if (securityStore.isInitialized()) {
        return securityStore.loadSecurityConfig().dbConfig;
    }

    throw new Error('缺少数据库配置。请设置 DB_HOST/DB_NAME/DB_USER/DB_PASSWORD，或在已初始化的部署目录中运行 migrate.js');
}

async function main() {
    const database = createDatabase(resolveDbConfig(), console);
    try {
        await database.initialize();
        console.info(`数据库 schema 已同步到 v${getLatestSchemaVersion()}`);
    } finally {
        await database.close().catch(() => {});
    }
}

main().catch((error) => {
    console.error('数据库 schema 迁移失败:', error.message);
    process.exitCode = 1;
});