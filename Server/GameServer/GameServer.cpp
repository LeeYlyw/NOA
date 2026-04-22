#include <iostream>
#include <cstring>
#include <WinSock2.h>

#pragma comment(lib, "ws2_32.lib")

int main()
{
    WSADATA wsaData;
    SOCKET listenSocket = INVALID_SOCKET;
    SOCKET clientSocket1 = INVALID_SOCKET;
    SOCKET clientSocket2 = INVALID_SOCKET;

    int result = WSAStartup(MAKEWORD(2, 2), &wsaData);
    if (result != 0)
    {
        std::cout << "WSAStartup failed: " << result << std::endl;
        return 1;
    }

    listenSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSocket == INVALID_SOCKET)
    {
        std::cout << "socket failed: " << WSAGetLastError() << std::endl;
        WSACleanup();
        return 1;
    }

    sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(7777);
    serverAddr.sin_addr.s_addr = htonl(INADDR_ANY);

    result = bind(listenSocket, (sockaddr*)&serverAddr, sizeof(serverAddr));
    if (result == SOCKET_ERROR)
    {
        std::cout << "bind failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    result = listen(listenSocket, SOMAXCONN);
    if (result == SOCKET_ERROR)
    {
        std::cout << "listen failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    std::cout << "Waiting for client 1..." << std::endl;
    clientSocket1 = accept(listenSocket, nullptr, nullptr);
    if (clientSocket1 == INVALID_SOCKET)
    {
        std::cout << "accept client 1 failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }
    std::cout << "Client 1 connected!" << std::endl;

    std::cout << "Waiting for client 2..." << std::endl;
    clientSocket2 = accept(listenSocket, nullptr, nullptr);
    if (clientSocket2 == INVALID_SOCKET)
    {
        std::cout << "accept client 2 failed: " << WSAGetLastError() << std::endl;
        closesocket(clientSocket1);
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }
    std::cout << "Client 2 connected!" << std::endl;

    char buffer[512];

    while (true)
    {
        fd_set readSet;
        FD_ZERO(&readSet);
        FD_SET(clientSocket1, &readSet);
        FD_SET(clientSocket2, &readSet);

        int selectResult = select(0, &readSet, nullptr, nullptr, nullptr);
        if (selectResult == SOCKET_ERROR)
        {
            std::cout << "select failed: " << WSAGetLastError() << std::endl;
            break;
        }

        if (FD_ISSET(clientSocket1, &readSet))
        {
            memset(buffer, 0, sizeof(buffer));
            int recvLength = recv(clientSocket1, buffer, sizeof(buffer) - 1, 0);

            if (recvLength > 0)
            {
                buffer[recvLength] = '\0';
                std::cout << "[1 -> 2] " << buffer << std::endl;
                if (send(clientSocket2, buffer, recvLength, 0) <= 0)
                {
                    std::cout << "send to client 2 failed: " << WSAGetLastError() << std::endl;
                    break;
                }
            }
            else
            {
                std::cout << "Client 1 disconnected or recv failed." << std::endl;
                break;
            }
        }

        if (FD_ISSET(clientSocket2, &readSet))
        {
            memset(buffer, 0, sizeof(buffer));
            int recvLength = recv(clientSocket2, buffer, sizeof(buffer) - 1, 0);

            if (recvLength > 0)
            {
                buffer[recvLength] = '\0';
                std::cout << "[2 -> 1] " << buffer << std::endl;
                if (send(clientSocket1, buffer, recvLength, 0) <= 0)
                {
                    std::cout << "send to client 1 failed: " << WSAGetLastError() << std::endl;
                    break;
                }
            }
            else
            {
                std::cout << "Client 2 disconnected or recv failed." << std::endl;
                break;
            }
        }
    }

    std::cout << "Press Enter to quit." << std::endl;
    std::cin.get();

    closesocket(clientSocket2);
    closesocket(clientSocket1);
    closesocket(listenSocket);
    WSACleanup();
    return 0;
}