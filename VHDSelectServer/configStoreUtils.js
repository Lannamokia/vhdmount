const fs = require('fs');
const path = require('path');

function createConfigWriteError(targetPath, error) {
    const reason = error instanceof Error ? error.message : String(error || 'unknown error');
    const wrapped = new Error(
        `配置目录写入失败，无法创建 ${targetPath}。如果 CONFIG_PATH 使用了 Docker bind mount，请确保宿主机目录允许容器内 nodejs 用户写入；在 Windows/macOS 上更稳妥的做法是使用 Docker named volume，或将目录放到 WSL2/ext4 路径。原始错误: ${reason}`,
    );

    if (error && typeof error === 'object' && 'code' in error) {
        wrapped.code = error.code;
    }

    wrapped.cause = error;
    return wrapped;
}

function ensureWritableDirectory(dirPath) {
    try {
        if (fs.existsSync(dirPath)) {
            const stats = fs.statSync(dirPath);
            if (!stats.isDirectory()) {
                throw new Error(`配置路径不是目录: ${dirPath}`);
            }
        } else {
            fs.mkdirSync(dirPath, { recursive: true });
        }
    } catch (error) {
        throw createConfigWriteError(dirPath, error);
    }

    const probeFile = path.join(dirPath, `.write-test-${process.pid}-${Date.now()}`);
    try {
        fs.writeFileSync(probeFile, '', { encoding: 'utf8', mode: 0o600 });
        fs.unlinkSync(probeFile);
    } catch (error) {
        try {
            if (fs.existsSync(probeFile)) {
                fs.unlinkSync(probeFile);
            }
        } catch {
            // Ignore cleanup failures and keep the original permission error.
        }
        throw createConfigWriteError(probeFile, error);
    }
}

function writeJsonAtomic(filePath, data) {
    ensureWritableDirectory(path.dirname(filePath));

    const tempFile = `${filePath}.tmp`;
    try {
        fs.writeFileSync(tempFile, JSON.stringify(data, null, 2), 'utf8');
        fs.renameSync(tempFile, filePath);
    } catch (error) {
        try {
            if (fs.existsSync(tempFile)) {
                fs.unlinkSync(tempFile);
            }
        } catch {
            // Ignore cleanup failures and keep the original permission error.
        }
        throw createConfigWriteError(tempFile, error);
    }
}

module.exports = {
    ensureWritableDirectory,
    writeJsonAtomic,
};