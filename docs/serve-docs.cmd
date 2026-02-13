@echo off
REM Serves the OpenSleigh docs site locally using Docker.
REM Usage: serve-docs.cmd
REM The site will be available at http://localhost:4000/OpenSleigh/

docker run --rm ^
  -v "%~dp0:/srv/jekyll" ^
  -w /srv/jekyll ^
  -p 4000:4000 ^
  ruby:3.3-slim ^
  sh -c "apt-get update -qq && apt-get install -y -qq build-essential > /dev/null 2>&1 && gem install bundler --silent && bundle install --quiet 2>/dev/null && bundle exec jekyll serve --host 0.0.0.0 --livereload"
