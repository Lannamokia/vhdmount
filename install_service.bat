@echo off
echo ========================================
echo VHD Mounter Windows����װ����
echo ========================================
echo.

:: ������ԱȨ��
net session >nul 2>&1
if %errorLevel% == 0 (
    echo ��⵽����ԱȨ�ޣ�����ִ��...
) else (
    echo ������Ҫ����ԱȨ�޲��ܰ�װWindows����
    echo ���Ҽ�������������ļ���ѡ��"�Թ���Ա��������"
    pause
    exit /b 1
)

echo.
echo ѡ�������
echo 1. ��װVHD Mounter����
echo 2. ж��VHD Mounter����
echo 3. ����VHD Mounter����
echo 4. ֹͣVHD Mounter����
echo 5. �鿴����״̬
echo 6. �˳�
echo.
set /p choice=������ѡ�� (1-6): 

if "%choice%"=="1" goto install
if "%choice%"=="2" goto uninstall
if "%choice%"=="3" goto start
if "%choice%"=="4" goto stop
if "%choice%"=="5" goto status
if "%choice%"=="6" goto exit
echo ��Чѡ�����������нű�
pause
exit /b 1

:install
echo.
echo ���ڰ�װVHD Mounter���񣨿����Զ�������...
sc create VHDMounterService binPath= "%~dp0VHDMounter.exe --service" start= auto DisplayName= "VHD Mounter Service" type= own
if %errorLevel% == 0 (
    echo ����װ�ɹ���
    sc description VHDMounterService "VHD�ļ��Զ����ط��񣨿����Զ��������������𴰿ڣ�"
    sc failure VHDMounterService reset= 86400 actions= restart/60000/restart/60000/restart/60000
    echo ����������ɣ������ÿ����Զ��������񣬷����Զ����𴰿ڣ�
    echo ���������������Ի�����Ȩ��...
    echo.
    set /p startNow=�Ƿ������������� (Y/N): 
    if /i "%startNow%"=="Y" (
        sc start VHDMounterService
        echo ����������ɣ�
    )
) else (
    echo ����װʧ�ܣ�
)
goto end

:uninstall
echo.
echo ����ж��VHD Mounter����...
sc stop VHDMounterService
sc delete VHDMounterService
if %errorLevel% == 0 (
    echo ����ж�سɹ���
) else (
    echo ����ж����ɣ�
)
goto end

:start
echo.
echo ��������VHD Mounter����...
sc start VHDMounterService
if %errorLevel% == 0 (
    echo ���������ɹ���
) else (
    echo ��������ʧ�ܣ�
)
goto end

:stop
echo.
echo ����ֹͣVHD Mounter����...
sc stop VHDMounterService
if %errorLevel% == 0 (
    echo ����ֹͣ�ɹ���
) else (
    echo ����ֹͣʧ�ܣ�
)
goto end

:status
echo.
echo VHD Mounter����״̬��
sc query VHDMounterService
goto end

:exit
echo �˳���װ����
exit /b 0

:end
echo.
echo ������ɣ�
pause
exit /b 0