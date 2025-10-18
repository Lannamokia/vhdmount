#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
VHD选择服务器 - Python版本
跨平台的VHD关键词管理服务器，带有Web GUI界面
"""

import json
import os
import sys
from datetime import datetime
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
import threading
import webbrowser
from pathlib import Path

class VHDSelectHandler(BaseHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        self.config_file = Path(__file__).parent / 'vhd-config.json'
        super().__init__(*args, **kwargs)
    
    def load_config(self):
        """加载VHD配置"""
        try:
            if self.config_file.exists():
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    config = json.load(f)
                    return config.get('vhdKeyword', 'SDEZ')
        except Exception as e:
            print(f"配置加载失败: {e}")
        return 'SDEZ'
    
    def save_config(self, vhd_keyword):
        """保存VHD配置"""
        try:
            config = {'vhdKeyword': vhd_keyword}
            with open(self.config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, ensure_ascii=False, indent=2)
            return True
        except Exception as e:
            print(f"配置保存失败: {e}")
            return False
    
    def send_json_response(self, data, status_code=200):
        """发送JSON响应"""
        self.send_response(status_code)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()
        
        json_str = json.dumps(data, ensure_ascii=False, indent=2)
        self.wfile.write(json_str.encode('utf-8'))
    
    def send_html_file(self, file_path):
        """发送HTML文件"""
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()
            
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.end_headers()
            self.wfile.write(content.encode('utf-8'))
        except FileNotFoundError:
            self.send_error(404, '文件未找到')
        except Exception as e:
            self.send_error(500, f'服务器错误: {e}')
    
    def do_OPTIONS(self):
        """处理CORS预检请求"""
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()
    
    def do_GET(self):
        """处理GET请求"""
        parsed_path = urlparse(self.path)
        path = parsed_path.path
        
        print(f"[{datetime.now().isoformat()}] GET {path}")
        
        if path == '/':
            # 主页
            html_file = Path(__file__).parent / 'public' / 'index.html'
            self.send_html_file(html_file)
            
        elif path == '/api/boot-image-select':
            # 获取当前VHD关键词
            current_keyword = self.load_config()
            response = {
                'success': True,
                'BootImageSelected': current_keyword,
                'timestamp': datetime.now().isoformat()
            }
            self.send_json_response(response)
            
        elif path == '/api/status':
            # 服务器状态
            current_keyword = self.load_config()
            response = {
                'success': True,
                'status': 'running',
                'BootImageSelected': current_keyword,
                'timestamp': datetime.now().isoformat(),
                'version': '1.0.0 (Python)'
            }
            self.send_json_response(response)
            
        else:
            # 404错误
            response = {
                'success': False,
                'error': '页面未找到'
            }
            self.send_json_response(response, 404)
    
    def do_POST(self):
        """处理POST请求"""
        parsed_path = urlparse(self.path)
        path = parsed_path.path
        
        print(f"[{datetime.now().isoformat()}] POST {path}")
        
        if path == '/api/set-vhd':
            try:
                # 读取请求体
                content_length = int(self.headers.get('Content-Length', 0))
                post_data = self.rfile.read(content_length)
                
                # 解析JSON数据
                data = json.loads(post_data.decode('utf-8'))
                vhd_keyword = data.get('BootImageSelected', '').strip().upper()
                
                if not vhd_keyword:
                    response = {
                        'success': False,
                        'error': 'VHD关键词不能为空'
                    }
                    self.send_json_response(response, 400)
                    return
                
                # 保存配置
                if self.save_config(vhd_keyword):
                    response = {
                        'success': True,
                        'BootImageSelected': vhd_keyword,
                        'message': 'VHD关键词更新成功'
                    }
                    print(f"VHD关键词已更新为: {vhd_keyword}")
                    self.send_json_response(response)
                else:
                    response = {
                        'success': False,
                        'error': '配置保存失败'
                    }
                    self.send_json_response(response, 500)
                    
            except json.JSONDecodeError:
                response = {
                    'success': False,
                    'error': '无效的JSON数据'
                }
                self.send_json_response(response, 400)
            except Exception as e:
                response = {
                    'success': False,
                    'error': f'服务器错误: {str(e)}'
                }
                self.send_json_response(response, 500)
        else:
            # 404错误
            response = {
                'success': False,
                'error': '页面未找到'
            }
            self.send_json_response(response, 404)
    
    def log_message(self, format, *args):
        """自定义日志格式"""
        pass  # 禁用默认日志，使用自定义日志

def start_server(port=8080):
    """启动服务器"""
    server_address = ('', port)
    httpd = HTTPServer(server_address, VHDSelectHandler)
    
    print('=' * 50)
    print('🚀 VHD选择服务器已启动 (Python版本)')
    print(f'📡 服务器地址: http://localhost:{port}')
    print(f'🔧 API地址: http://localhost:{port}/api/boot-image-select')
    print(f'📊 状态页面: http://localhost:{port}/api/status')
    
    # 加载当前配置
    config_file = Path(__file__).parent / 'vhd-config.json'
    try:
        if config_file.exists():
            with open(config_file, 'r', encoding='utf-8') as f:
                config = json.load(f)
                current_keyword = config.get('vhdKeyword', 'SDEZ')
        else:
            current_keyword = 'SDEZ'
    except:
        current_keyword = 'SDEZ'
    
    print(f'🎯 当前VHD关键词: {current_keyword}')
    print('=' * 50)
    print('💡 提示: 按 Ctrl+C 停止服务器')
    print()
    
    # 自动打开浏览器
    def open_browser():
        import time
        time.sleep(1)  # 等待服务器启动
        try:
            webbrowser.open(f'http://localhost:{port}')
        except:
            pass
    
    browser_thread = threading.Thread(target=open_browser)
    browser_thread.daemon = True
    browser_thread.start()
    
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print('\n正在关闭服务器...')
        httpd.shutdown()
        print('服务器已关闭')

if __name__ == '__main__':
    # 检查命令行参数
    port = 8080
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            print('错误: 端口号必须是数字')
            sys.exit(1)
    
    start_server(port)