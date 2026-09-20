FROM ubuntu:22.04@sha256:b8b6ee6aa931ecd9d0d952abc34dc0e5f7c6a30c6bb71b079fe399fde0329c02

ARG OPENSSH_VERSION=1:8.9p1-3ubuntu0.17
RUN apt-get -o Acquire::Retries=2 -o Acquire::http::Timeout=30 update \
    && apt-cache policy openssh-server
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        openssh-server=${OPENSSH_VERSION} openssh-client=${OPENSSH_VERSION} \
        openssh-sftp-server=${OPENSSH_VERSION} python3 \
    && useradd --create-home --shell /bin/sh tunneltest \
    && mkdir -p /run/sshd /fixture-output

COPY fixture_server.py /opt/tunneldeck/fixture_server.py
EXPOSE 2222 2223 2224 2225 8080
ENTRYPOINT ["python3", "/opt/tunneldeck/fixture_server.py"]
