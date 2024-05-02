FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim as builder
WORKDIR /build
COPY . .
RUN dotnet publish --configuration Release --output publish
FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim
# ARG CODENAME=bookworm
# RUN apt-get update
# RUN apt-get -y -qq install curl gpg
# RUN curl --silent https://packages.fluentbit.io/fluentbit.key | gpg --dearmor > /usr/share/keyrings/fluentbit-keyring.gpg
# RUN echo "deb [signed-by=/usr/share/keyrings/fluentbit-keyring.gpg] https://packages.fluentbit.io/debian/${CODENAME} ${CODENAME} main" >>/etc/apt/sources.list
# RUN apt-get update
# RUN apt-get -y -qq install fluent-bit
WORKDIR /app
COPY --from=builder /build/publish/ .
ENV ECSLYPO_DECREMENT_ASG_CAPACITY=true
ENV ECSLYPO_IS_ENABLED=true
ENV ECSLYPO_RUNNING_TASK=4
ENV ECSLYPO_ECS_CLUSTER=""
ENV ASPNETCORE_HTTP_PORTS=80
ENTRYPOINT ["dotnet", "ECSLypo.CLI.dll"]