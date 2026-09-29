# Ambiente de desenvolvimento e de build do TradeRock (também usado no CI).
# O produto final é AppImage/.deb (e instalador no Windows), nunca esta imagem.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

ARG NODE_VERSION=24.21.0
ARG USERNAME=dev
ARG USER_UID=1000
ARG USER_GID=1000

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    TRADEROCK_CONTAINER=1

# - python3 (3.12 no Ubuntu 24.04) + venv: motor Python e esptool
# - webkit2gtk/gtk/libnotify: janela Photino (quando houver display repassado)
# - dpkg-dev, fakeroot, file, libfuse2, desktop-file-utils: empacotamento .deb/AppImage (Entrega 8)
# - patch: aplica os patches de engines/patches (motor Python no build do worker, núcleo C# do ESP32 no dotnet build)
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl xz-utils git sudo patch \
        python3 python3-venv python3-pip \
        libwebkit2gtk-4.1-dev libgtk-3-dev libnotify-dev \
        dpkg-dev fakeroot file libfuse2t64 desktop-file-utils squashfs-tools \
        iw usbutils \
    && rm -rf /var/lib/apt/lists/*

# Node LTS a partir do tarball oficial, conferido pelo SHASUMS256.txt.
RUN set -eux; \
    cd /tmp; \
    curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz"; \
    curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/SHASUMS256.txt"; \
    grep " node-v${NODE_VERSION}-linux-x64.tar.xz\$" SHASUMS256.txt | sha256sum -c -; \
    tar -xJf "node-v${NODE_VERSION}-linux-x64.tar.xz" -C /usr/local --strip-components=1; \
    rm -f node-*.tar.xz SHASUMS256.txt; \
    node --version

# esptool num venv próprio (não mexe no Python do sistema).
RUN python3 -m venv /opt/esptool && /opt/esptool/bin/pip install --no-cache-dir esptool \
    && ln -s /opt/esptool/bin/esptool /usr/local/bin/esptool

# Usuário sem root, com o mesmo UID do host para os arquivos do repositório.
# A imagem base do Ubuntu 24.04 já traz o usuário "ubuntu" com UID 1000.
RUN if id -u ubuntu >/dev/null 2>&1; then userdel -r ubuntu; fi; \
    groupadd --gid ${USER_GID} ${USERNAME}; \
    useradd --uid ${USER_UID} --gid ${USER_GID} -m -s /bin/bash ${USERNAME}; \
    usermod -aG dialout ${USERNAME}; \
    echo "${USERNAME} ALL=(root) NOPASSWD:ALL" > /etc/sudoers.d/${USERNAME}; \
    chmod 0440 /etc/sudoers.d/${USERNAME}

USER ${USERNAME}
WORKDIR /workspace
