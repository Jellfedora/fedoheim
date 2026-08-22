ALTER TABLE `modpacks` ADD `is_default` integer DEFAULT false NOT NULL;
--> statement-breakpoint
UPDATE `modpacks` SET `is_default` = 1 WHERE `slug` = 'default';