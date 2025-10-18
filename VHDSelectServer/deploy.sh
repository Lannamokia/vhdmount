#!/bin/bash

echo "VHD Select Server Docker 部署脚本"
echo "================================"

# 检查Docker是否安装
if ! command -v docker &> /dev/null; then
    echo "❌ 错误: Docker未安装"
    echo "请先安装Docker: https://docs.docker.com/get-docker/"
    exit 1
fi

# 检查Docker是否运行
if ! docker info &> /dev/null; then
    echo "❌ 错误: Docker未运行"
    echo "请启动Docker服务"
    exit 1
fi

echo "✅ Docker检查通过"

# 检查Docker Compose
if ! command -v docker-compose &> /dev/null; then
    echo "❌ 错误: Docker Compose未安装"
    echo "请安装Docker Compose"
    exit 1
fi

echo "✅ Docker Compose检查通过"

# 创建配置目录
echo "📁 创建配置目录..."
mkdir -p config

# 拉取并启动服务
echo "🚀 拉取并启动服务..."
if docker-compose up -d; then
    echo ""
    echo "✅ 部署成功!"
    echo "🌐 Web界面: http://localhost:8080"
    echo "📁 配置文件: ./config/vhd-config.json"
    echo ""
    echo "常用命令:"
    echo "  查看日志: docker-compose logs -f"
    echo "  停止服务: docker-compose down"
    echo "  重启服务: docker-compose restart"
    echo "  查看状态: docker-compose ps"
else
    echo ""
    echo "❌ 部署失败，请检查错误信息"
    echo "如果是网络问题，请尝试:"
    echo "  1. 检查网络连接"
    echo "  2. 配置Docker镜像源"
    echo "  3. 使用本地Node.js部署: npm start"
    exit 1
fi

echo ""
echo "🎉 部署完成!"