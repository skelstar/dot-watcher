FROM node:22-alpine AS build
WORKDIR /app
COPY package.json ./
RUN yarn install
COPY . .
ARG VITE_APP_VERSION
ENV VITE_APP_VERSION=$VITE_APP_VERSION
# The LINZ map key reaches the build one of two ways: client/.env in the build context (how a local deploy
# gets it), or this build argument (for a build that has no .env, such as a clean checkout). It is written
# to a file only when given, because setting the variable unconditionally would blank out a real key in
# .env whenever no argument is passed. vite.config.ts fails the build if neither supplies one.
ARG VITE_LINZ_API_KEY
RUN if [ -n "$VITE_LINZ_API_KEY" ]; then echo "VITE_LINZ_API_KEY=$VITE_LINZ_API_KEY" > .env.production.local; fi \
    && yarn build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
