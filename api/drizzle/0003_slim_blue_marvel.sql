ALTER TABLE `users` ADD `is_banned` integer DEFAULT false NOT NULL;--> statement-breakpoint
ALTER TABLE `users` ADD `rules_accepted_at` integer;--> statement-breakpoint
ALTER TABLE `users` ADD `steam_id` text;